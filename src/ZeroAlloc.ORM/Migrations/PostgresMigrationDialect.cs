using System.Collections.Generic;
using System.Data.Async;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.ORM.Migrations;

/// <summary>
/// v1.1 Phase B.1 — <see cref="IMigrationDialect"/> implementation for Postgres
/// (via Npgsql). The history table uses Postgres-native types: <c>INTEGER</c>
/// primary key for <c>version</c>, <c>TEXT NOT NULL</c> for <c>name</c>, and
/// <c>TIMESTAMPTZ NOT NULL DEFAULT NOW()</c> for <c>applied_at</c> (preserves
/// timezone info; <see cref="SqliteMigrationDialect"/>'s TEXT storage drops it).
///
/// <para>
/// Lock semantics use <c>pg_advisory_lock(bigint)</c>: a session-scoped
/// cooperative lock that blocks until acquired. This lets multi-instance API
/// startup serialize concurrent <see cref="MigrationRunner.RunAsync"/> calls
/// without a separate Lease/Mutex service. The lock is released explicitly in
/// <see cref="ReleaseLockAsync"/>; if the holding session terminates, Postgres
/// releases the lock automatically.
/// </para>
///
/// <para>
/// The <c>InsertAppliedVersionSql</c> casts the <c>@applied_at</c> parameter to
/// <c>timestamptz</c> via the <c>::timestamptz</c> suffix — the
/// <see cref="MigrationRunner"/> binds <c>@applied_at</c> as an ISO-8601 string
/// (matching the Sqlite TEXT convention), and Postgres performs the implicit
/// parse on insert.
/// </para>
///
/// <para>
/// #306 — versions are scoped by source; see <see cref="IScopedMigrationDialect"/>. The
/// primary key is <c>(source, version)</c>. A history table from before scoping is upgraded
/// in place: the <c>source</c> column is added and filled, and the primary key is replaced,
/// in one transaction.
/// </para>
/// </summary>
public sealed class PostgresMigrationDialect : IScopedMigrationDialect
{
    private static readonly string[] UpgradeStatements =
    [
        "ALTER TABLE __zaorm_migrations ADD COLUMN source TEXT",
        "UPDATE __zaorm_migrations SET source = @source",
        "ALTER TABLE __zaorm_migrations ALTER COLUMN source SET NOT NULL",
        // The name PostgreSQL gave the key of the table this dialect created before
        // scoping. A table with another key fails here, and the upgrade rolls back.
        "ALTER TABLE __zaorm_migrations DROP CONSTRAINT __zaorm_migrations_pkey",
        "ALTER TABLE __zaorm_migrations ADD PRIMARY KEY (source, version)",
    ];

    /// <summary>
    /// Default 64-bit constant passed to <c>pg_advisory_lock</c>. Packs the
    /// ASCII bytes of <c>"ZAORM_MI"</c> (8 bytes) into a <see cref="long"/> —
    /// <c>0x5A41_4F52_4D5F_4D49</c>. Adopters whose process already uses
    /// advisory locks with a colliding constant can pass a different value via
    /// the <see cref="PostgresMigrationDialect(long)"/> constructor.
    /// </summary>
    public const long DefaultLockKey = 0x5A41_4F52_4D5F_4D49L;

    private readonly long _lockKey;

    /// <summary>
    /// Creates a Postgres dialect with the supplied advisory-lock key. Defaults
    /// to <see cref="DefaultLockKey"/>; override only when an adopter's process
    /// already uses <c>pg_advisory_lock</c> with the same constant.
    /// </summary>
    public PostgresMigrationDialect(long lockKey = DefaultLockKey)
    {
        _lockKey = lockKey;
    }

    /// <inheritdoc />
    public string CreateHistoryTableSql =>
        "CREATE TABLE IF NOT EXISTS __zaorm_migrations (" +
        "source TEXT NOT NULL, " +
        "version INTEGER NOT NULL, " +
        "name TEXT NOT NULL, " +
        "applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), " +
        "PRIMARY KEY (source, version))";

    /// <inheritdoc />
    public string SelectAppliedVersionsSql =>
        "SELECT version, name FROM __zaorm_migrations WHERE source = @source ORDER BY version";

    /// <inheritdoc />
    public string InsertAppliedVersionSql =>
        "INSERT INTO __zaorm_migrations (source, version, name, applied_at) " +
        "VALUES (@source, @version, @name, @applied_at::timestamptz)";

    /// <inheritdoc />
    /// <remarks>Looks the table up in <c>current_schema()</c>, where an unqualified
    /// <c>CREATE TABLE</c> puts it.</remarks>
    public string SelectUnscopedHistorySql =>
        "SELECT CASE WHEN EXISTS (SELECT 1 FROM information_schema.columns " +
        "WHERE table_schema = current_schema() AND table_name = '__zaorm_migrations' " +
        "AND column_name = 'source') THEN 0 ELSE 1 END";

    /// <inheritdoc />
    public IReadOnlyList<string> UpgradeUnscopedHistorySql => UpgradeStatements;

    /// <inheritdoc />
    public async Task AcquireLockAsync(IAsyncDbConnection connection, CancellationToken ct)
    {
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT pg_advisory_lock(@key)";
            var p = cmd.CreateParameter();
            p.ParameterName = "key";
            p.Value = _lockKey;
            cmd.Parameters.Add(p);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task ReleaseLockAsync(IAsyncDbConnection connection, CancellationToken ct)
    {
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT pg_advisory_unlock(@key)";
            var p = cmd.CreateParameter();
            p.ParameterName = "key";
            p.Value = _lockKey;
            cmd.Parameters.Add(p);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }
}
