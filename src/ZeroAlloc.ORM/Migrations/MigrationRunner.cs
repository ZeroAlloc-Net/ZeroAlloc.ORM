using System;
using System.Collections.Generic;
using System.Data.Async;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.ORM.Migrations;

/// <summary>
/// v1.1 — Orchestrates discovery-then-apply for a set of migrations against a
/// caller-supplied <see cref="IAsyncDbConnection"/>. The runner is a one-shot
/// utility (no shared mutable state); callers construct it with the three
/// collaborators (connection, source, dialect) and invoke <see cref="RunAsync"/>
/// once.
///
/// <para>
/// Algorithm (per <c>docs/plans/2026-06-01-v1.1-implementation.md</c> Task A.3):
/// </para>
/// <list type="number">
///   <item>Acquire the dialect's apply-lock (no-op on Sqlite).</item>
///   <item>Execute <see cref="IMigrationDialect.CreateHistoryTableSql"/> — idempotent.</item>
///   <item>Get migrations from <see cref="IMigrationSource.GetMigrations"/>, and reject
///         (<see cref="ZeroAllocOrmMigrationConflictException"/>) two of them sharing a
///         version.</item>
///   <item>#306 — for an <see cref="IScopedMigrationDialect"/>, upgrade a history table
///         from before scoping, in one transaction; see <see cref="RunAsync"/>.</item>
///   <item>Read the source's applied versions via
///         <see cref="IMigrationDialect.SelectAppliedVersionsSql"/> into a
///         <see cref="Dictionary{TKey, TValue}"/> of version → recorded name. Reject any
///         discovered version already applied under a different name — see
///         <see cref="RunAsync"/>. Otherwise filter to pending (version NOT in applied
///         map, or applied under the same name is a silent replay), sort ascending.</item>
///   <item>For each pending migration: BEGIN TRANSACTION, execute the migration
///         body, INSERT the history row, COMMIT. On exception: ROLLBACK,
///         release the lock, and rethrow. If the ROLLBACK also throws, its
///         exception is attached to the rethrown one under
///         <see cref="RollbackExceptionDataKey"/>.</item>
///   <item>Release the dialect's apply-lock and return the applied list with
///         <see cref="Migration.AppliedAt"/> populated to UTC now.</item>
/// </list>
///
/// <para>
/// The runner does NOT auto-open the connection — callers are expected to pass
/// an already-open <see cref="IAsyncDbConnection"/> (matching the rest of the
/// ZA.ORM substrate's lifecycle contract).
/// </para>
/// </summary>
public sealed class MigrationRunner
{
    /// <summary>
    /// The <see cref="Exception.Data"/> key under which <see cref="RunAsync"/> records a
    /// rollback failure. When a migration fails and rolling back its transaction also
    /// throws, the runner still rethrows the migration's original exception unchanged,
    /// and stores the rollback exception in that exception's <see cref="Exception.Data"/>
    /// under this key. Its presence means the rollback did not complete, so the database
    /// may be in an unknown state. The entry is absent when the rollback succeeded, and
    /// is not added when the original exception's <see cref="Exception.Data"/> is
    /// read-only or fixed-size.
    /// </summary>
    public const string RollbackExceptionDataKey = "ZeroAlloc.ORM.Migrations.MigrationRunner.RollbackException";

    /// <summary>
    /// The longest <see cref="IMigrationSource.Name"/> the runner accepts. The built-in SQL
    /// Server dialect stores the name in an <c>NVARCHAR(256)</c> key column.
    /// </summary>
    public const int MaxSourceNameLength = 256;

    private readonly IAsyncDbConnection _connection;
    private readonly IMigrationSource _source;
    private readonly IMigrationDialect _dialect;
    private readonly string _sourceName;

    /// <summary>
    /// Creates a one-shot runner bound to the supplied collaborators. None of
    /// the three may be null; the runner holds them by reference and does not
    /// take ownership of the connection (caller disposes).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/>'s <see cref="IMigrationSource.Name"/> is null, empty,
    /// whitespace, or longer than <see cref="MaxSourceNameLength"/>.
    /// </exception>
    public MigrationRunner(IAsyncDbConnection connection, IMigrationSource source, IMigrationDialect dialect)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        _sourceName = ValidateSourceName(source);
    }

    private static string ValidateSourceName(IMigrationSource source)
    {
        var name = source.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                $"The migration source {source.GetType()} has an empty Name. The name scopes the " +
                "source's versions in the history table; give it a stable, non-empty one.",
                nameof(source));
        }

        if (name.Length > MaxSourceNameLength)
        {
            throw new ArgumentException(
                $"The migration source name '{name}' is {name.Length} characters long; at most " +
                $"{MaxSourceNameLength} are allowed.",
                nameof(source));
        }

        return name;
    }

    /// <summary>
    /// Executes the migration pipeline and returns the set of migrations newly
    /// applied during this invocation (each with <see cref="Migration.AppliedAt"/>
    /// populated). A migration whose <c>Version</c> AND <c>Name</c> already match
    /// a row in the history table is a genuine replay and is skipped silently
    /// (NOT included in the return list).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #306 — with an <see cref="IScopedMigrationDialect"/>, which every built-in dialect is,
    /// versions are scoped by the source's <see cref="IMigrationSource.Name"/>: each source
    /// only sees, and only conflicts with, the versions it applied itself. Two sources that
    /// both number from 1 can then migrate one database.
    /// </para>
    /// <para>
    /// A history table from before scoping keeps one version sequence for the whole database.
    /// The first run against it upgrades the table in place, in one transaction, and assigns
    /// every existing row to the source being run: that table was written by one source, or by
    /// one combined source, and the source that wrote it is the one expected to run next. The
    /// upgrade is checked before it commits: every row must be one of the source's migrations,
    /// by version and name. Otherwise the rows belong, at least in part, to another source; the
    /// transaction rolls back, leaving the table as it was, and a
    /// <see cref="ZeroAllocOrmMigrationConflictException"/> lists the rows and says to run the
    /// source that wrote them first.
    /// </para>
    /// </remarks>
    /// <exception cref="ZeroAllocOrmMigrationConflictException">
    /// Thrown instead of silently skipping when a discovered migration's
    /// <c>Version</c> is already recorded as applied under a DIFFERENT
    /// <c>Name</c> — a version-number collision, not a replay. Also thrown when
    /// two discovered migrations share the same <c>Version</c>, regardless of
    /// whether either has been applied, and when a history table from before scoping
    /// does not belong to this source.
    /// </exception>
    /// <exception cref="System.Data.Common.DbException">
    /// Propagated verbatim when a migration's SQL fails. The transaction for the
    /// failing migration is rolled back; earlier migrations in this call have
    /// already committed and remain in the history table; later migrations are
    /// never attempted. If the rollback itself throws, the original exception is
    /// still the one propagated, and the rollback exception is stored in its
    /// <see cref="Exception.Data"/> under <see cref="RollbackExceptionDataKey"/>.
    /// </exception>
    public async Task<IReadOnlyList<Migration>> RunAsync(CancellationToken ct = default)
    {
        await _dialect.AcquireLockAsync(_connection, ct).ConfigureAwait(false);
        try
        {
            // Step 2: bootstrap the history table (CREATE IF NOT EXISTS).
            await ExecuteNonQueryAsync(_dialect.CreateHistoryTableSql, ct).ConfigureAwait(false);

            // Step 3: discover, and reject two migrations sharing a version before
            // anything is written.
            var discovered = _source.GetMigrations();
            RejectDuplicateVersions(discovered);

            // Step 4: #306 — move a history table from before scoping to the scoped
            // layout, assigning its rows to this source.
            if (_dialect is IScopedMigrationDialect scoped)
            {
                await UpgradeUnscopedHistoryAsync(scoped, discovered, ct).ConfigureAwait(false);
            }

            // Step 5: snapshot this source's applied versions, keyed by recorded name,
            // then filter + sort.
            var applied = await ReadAppliedVersionsAsync(tx: null, ct).ConfigureAwait(false);
            var pending = ResolvePending(discovered, applied);

            // Step 6: per-migration tx — commit each individually so a downstream
            // failure leaves earlier migrations applied (Phase A.3 invariant).
            var result = new List<Migration>(pending.Count);
            foreach (var migration in pending)
            {
                var appliedAt = await ApplyOneAsync(migration, ct).ConfigureAwait(false);
                result.Add(migration with { AppliedAt = appliedAt });
            }

            return result;
        }
        finally
        {
            // Step 7: always release the lock — even when the apply loop threw.
            // CancellationToken.None here so the release survives token cancellation;
            // the dialect's release semantics are responsible for any post-cancel
            // cleanup decisions.
            await _dialect.ReleaseLockAsync(_connection, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Two discovered migrations sharing a version is always a bug — reject it up front,
    /// independent of what is or isn't applied yet.
    /// </summary>
    /// <exception cref="ZeroAllocOrmMigrationConflictException">
    /// Thrown when two discovered migrations share a version.
    /// </exception>
    private static void RejectDuplicateVersions(IReadOnlyList<Migration> discovered)
    {
        var byVersion = new Dictionary<int, Migration>(discovered.Count);
        foreach (var m in discovered)
        {
            if (byVersion.TryGetValue(m.Version, out var other))
            {
                throw new ZeroAllocOrmMigrationConflictException(
                    $"Migration version {m.Version} is used by two discovered migrations: " +
                    $"'{other.Name}' and '{m.Name}'. Renumber one of them so every " +
                    "discovered migration has a unique version.");
            }
            byVersion[m.Version] = m;
        }
    }

    /// <summary>
    /// #306 — moves a history table from before scoping to the scoped layout, assigning
    /// every existing row to this source, in one transaction. Does nothing when the table
    /// is already scoped. The check for the old layout runs inside the transaction, so a
    /// concurrent runner that upgraded first is seen.
    /// </summary>
    /// <exception cref="ZeroAllocOrmMigrationConflictException">
    /// Thrown, after the transaction rolls back, when a row of the table is not one of this
    /// source's migrations by version and name.
    /// </exception>
    private async Task UpgradeUnscopedHistoryAsync(
        IScopedMigrationDialect dialect,
        IReadOnlyList<Migration> discovered,
        CancellationToken ct)
    {
        var tx = await _connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (tx.ConfigureAwait(false))
        {
            try
            {
                var unscoped = await ExecuteScalarOnTransactionAsync(tx, dialect.SelectUnscopedHistorySql, ct)
                    .ConfigureAwait(false);
                if (Convert.ToInt64(unscoped, CultureInfo.InvariantCulture) != 0)
                {
                    foreach (var statement in dialect.UpgradeUnscopedHistorySql)
                    {
                        await ExecuteOnTransactionAsync(tx, statement, SourceParameters(), ct).ConfigureAwait(false);
                    }

                    var assigned = await ReadAppliedVersionsAsync(tx, ct).ConfigureAwait(false);
                    CheckUpgradedRowsBelongToSource(assigned, discovered);
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try
                {
                    await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackEx)
                {
                    AttachRollbackFailure(ex, rollbackEx);
                }
                throw;
            }
        }
    }

    // #306 — every row of a history table from before scoping must be one of this source's
    // migrations, by version and name, before the upgrade assigns the rows to it. A row it
    // does not have may belong to another source that shared the table with distinct version
    // numbers; assigning it here would make that source apply its migrations again. So the
    // upgrade is refused instead, and the transaction rolls back.
    private void CheckUpgradedRowsBelongToSource(Dictionary<int, string> assigned, IReadOnlyList<Migration> discovered)
    {
        var names = new Dictionary<int, string>(discovered.Count);
        foreach (var m in discovered)
        {
            names[m.Version] = m.Name;
        }

        var foreign = new List<string>();
        foreach (var row in assigned)
        {
            if (!names.TryGetValue(row.Key, out var name) || !string.Equals(name, row.Value, StringComparison.Ordinal))
            {
                foreign.Add(row.Key.ToString(CultureInfo.InvariantCulture) + " '" + row.Value + "'");
            }
        }

        if (foreign.Count == 0)
        {
            return;
        }

        foreign.Sort(StringComparer.Ordinal);
        const int Shown = 5;
        var list = string.Join(", ", foreign.GetRange(0, Math.Min(Shown, foreign.Count)))
            + (foreign.Count > Shown ? $" and {foreign.Count - Shown} more" : string.Empty);
        throw new ZeroAllocOrmMigrationConflictException(
            "The migration history table was created before migration versions were scoped by " +
            $"source. Upgrading it assigns every row to the source being run, '{_sourceName}', " +
            $"but {foreign.Count} of its {assigned.Count} rows are not migrations of this source, " +
            $"by version and name: {list}. The history table is unchanged. Run the source that " +
            "wrote the table first, so the upgrade assigns the rows to it. If several sources " +
            "wrote it, see \"Upgrading a history table from before source scoping\" in the " +
            "migrations cookbook.");
    }

    /// <summary>
    /// Validates the discovered migrations against the applied-versions map, then returns
    /// the ones still pending, sorted by <see cref="Migration.Version"/> ascending.
    /// </summary>
    /// <exception cref="ZeroAllocOrmMigrationConflictException">
    /// Thrown when a discovered version is already applied under a different name.
    /// </exception>
    private static List<Migration> ResolvePending(IReadOnlyList<Migration> discovered, Dictionary<int, string> applied)
    {
        // A discovered version already recorded as applied under a different
        // name is a version collision, not a replay — throw. The same version
        // with the same name is a genuine replay and is skipped silently,
        // matching today's behaviour.
        var pending = new List<Migration>(discovered.Count);
        foreach (var m in discovered)
        {
            if (applied.TryGetValue(m.Version, out var appliedName))
            {
                if (!string.Equals(appliedName, m.Name, StringComparison.Ordinal))
                {
                    throw new ZeroAllocOrmMigrationConflictException(
                        $"Migration version {m.Version} is already applied as " +
                        $"'{appliedName}', but the discovered migration for that version " +
                        $"is named '{m.Name}'. Renumber the new migration to an unused " +
                        "version — do not reuse an applied version number.");
                }

                continue;
            }

            pending.Add(m);
        }
        pending.Sort(static (a, b) => a.Version.CompareTo(b.Version));
        return pending;
    }

    private async Task<DateTime> ApplyOneAsync(Migration migration, CancellationToken ct)
    {
        var tx = await _connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (tx.ConfigureAwait(false))
        {
            try
            {
                // 5a: run the migration body. Multi-statement scripts are handled
                // by the underlying provider (Microsoft.Data.Sqlite + Npgsql both
                // accept multi-statement command text).
                await ExecuteOnTransactionAsync(tx, migration.Sql, parameters: null, ct).ConfigureAwait(false);

                // 5b: record the row in the history table. UTC ISO-8601 timestamp
                // matches the Sqlite dialect's TEXT storage convention; the
                // Postgres dialect (Phase B) will swap this to a DateTime param.
                var appliedAt = DateTime.UtcNow;
                var insertParameters = new List<MigrationParameter>(4)
                {
                    new MigrationParameter("version", migration.Version),
                    new MigrationParameter("name", migration.Name),
                    new MigrationParameter("applied_at", appliedAt.ToString("o", CultureInfo.InvariantCulture)),
                };
                insertParameters.AddRange(SourceParameters());
                await ExecuteOnTransactionAsync(tx, _dialect.InsertAppliedVersionSql, insertParameters, ct)
                    .ConfigureAwait(false);

                await tx.CommitAsync(ct).ConfigureAwait(false);
                return appliedAt;
            }
            catch (Exception ex)
            {
                // Cancellation-aware rollback: BeginTransactionAsync sets the
                // transaction's Connection; the underlying provider's
                // RollbackAsync handles already-aborted state gracefully. Using
                // CancellationToken.None so rollback completes even if the outer
                // token was cancelled.
                try
                {
                    await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackEx)
                {
                    // The original exception stays the one callers see (it points
                    // at the failing migration's SQL), but a failed rollback means
                    // the database may be in an unknown state, so record it on the
                    // original rather than losing it.
                    AttachRollbackFailure(ex, rollbackEx);
                }
                throw;
            }
        }
    }

    /// <summary>
    /// Stores <paramref name="rollbackFailure"/> in <paramref name="original"/>'s
    /// <see cref="Exception.Data"/> under <see cref="RollbackExceptionDataKey"/>. Skipped
    /// when that dictionary cannot take the entry — read-only, or fixed-size without the
    /// key — so this never throws from inside the catch handler and never replaces the
    /// original exception.
    /// </summary>
    private static void AttachRollbackFailure(Exception original, Exception rollbackFailure)
    {
        var data = original.Data;
        if (data.IsReadOnly || (data.IsFixedSize && !data.Contains(RollbackExceptionDataKey)))
        {
            return;
        }

        data[RollbackExceptionDataKey] = rollbackFailure;
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken ct)
    {
        var cmd = _connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ExecuteOnTransactionAsync(IAsyncDbTransaction tx, string sql, IReadOnlyList<MigrationParameter>? parameters, CancellationToken ct)
    {
        var cmd = _connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            AddParameters(cmd, parameters);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task<object?> ExecuteScalarOnTransactionAsync(IAsyncDbTransaction tx, string sql, CancellationToken ct)
    {
        var cmd = _connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        }
    }

    private static void AddParameters(IAsyncDbCommand cmd, IReadOnlyList<MigrationParameter>? parameters)
    {
        if (parameters is null)
        {
            return;
        }

        foreach (var p in parameters)
        {
            var dbp = cmd.CreateParameter();
            dbp.ParameterName = p.Name;
            dbp.Value = p.Value;
            cmd.Parameters.Add(dbp);
        }
    }

    // #306 — the `source` parameter a scoped dialect's history statements take. Empty for
    // a dialect that keeps one version sequence per database.
    private MigrationParameter[] SourceParameters()
        => _dialect is IScopedMigrationDialect
            ? [new MigrationParameter("source", _sourceName)]
            : [];

    private async Task<Dictionary<int, string>> ReadAppliedVersionsAsync(IAsyncDbTransaction? tx, CancellationToken ct)
    {
        var map = new Dictionary<int, string>();
        var cmd = _connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            if (tx is not null)
            {
                cmd.Transaction = tx;
            }
            cmd.CommandText = _dialect.SelectAppliedVersionsSql;
            AddParameters(cmd, SourceParameters());
            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (((IAsyncDisposable)reader).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    map.Add(reader.GetInt32(0), reader.GetString(1));
                }
            }
        }
        return map;
    }

    // Lightweight parameter carrier so the per-migration apply step doesn't
    // touch ADO.NET parameter types directly. Kept private — internal-only
    // detail of the runner.
    private readonly struct MigrationParameter
    {
        public string Name { get; }
        public object Value { get; }
        public MigrationParameter(string name, object value)
        {
            Name = name;
            Value = value;
        }
    }
}
