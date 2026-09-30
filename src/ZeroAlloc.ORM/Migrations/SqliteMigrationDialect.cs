using System.Collections.Generic;
using System.Data.Async;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.ORM.Migrations;

/// <summary>
/// v1.1 — <see cref="IMigrationDialect"/> implementation for Sqlite (via
/// Microsoft.Data.Sqlite). The history table uses Sqlite type affinities:
/// <c>TEXT NOT NULL</c> for <c>source</c> and <c>name</c>, <c>INTEGER</c> for
/// <c>version</c>, with <c>(source, version)</c> as the primary key, and ISO-8601
/// <c>TEXT NOT NULL</c> for <c>applied_at</c>
/// (Sqlite stores timestamps as text by convention; the runner writes them
/// via <c>DateTime.UtcNow.ToString("o")</c>).
///
/// <para>
/// #306 — versions are scoped by source; see <see cref="IScopedMigrationDialect"/>. SQLite
/// cannot change a table's primary key, so the upgrade of a history table from before
/// scoping copies it into a new table and renames that into place, in one transaction.
/// </para>
///
/// <para>
/// Lock semantics are no-ops: Sqlite serializes writers natively (BEGIN
/// EXCLUSIVE / journal / WAL), so the per-migration transaction inside
/// <see cref="MigrationRunner"/> is sufficient for atomicity. Postgres
/// (Phase B) will switch to <c>pg_advisory_lock</c>.
/// </para>
/// </summary>
public sealed class SqliteMigrationDialect : IScopedMigrationDialect
{
    private static readonly string[] UpgradeStatements =
    [
        "CREATE TABLE __zaorm_migrations_scoped (" +
        "source TEXT NOT NULL," +
        "version INTEGER NOT NULL," +
        "name TEXT NOT NULL," +
        "applied_at TEXT NOT NULL," +
        "PRIMARY KEY (source, version))",
        "INSERT INTO __zaorm_migrations_scoped (source, version, name, applied_at) " +
        "SELECT @source, version, name, applied_at FROM __zaorm_migrations",
        "DROP TABLE __zaorm_migrations",
        "ALTER TABLE __zaorm_migrations_scoped RENAME TO __zaorm_migrations",
    ];

    /// <inheritdoc />
    public string CreateHistoryTableSql =>
        "CREATE TABLE IF NOT EXISTS __zaorm_migrations (" +
        "source TEXT NOT NULL," +
        "version INTEGER NOT NULL," +
        "name TEXT NOT NULL," +
        "applied_at TEXT NOT NULL," +
        "PRIMARY KEY (source, version))";

    /// <inheritdoc />
    public string SelectAppliedVersionsSql =>
        "SELECT version, name FROM __zaorm_migrations WHERE source = @source ORDER BY version";

    /// <inheritdoc />
    public string InsertAppliedVersionSql =>
        "INSERT INTO __zaorm_migrations (source, version, name, applied_at) " +
        "VALUES (@source, @version, @name, @applied_at)";

    /// <inheritdoc />
    public string SelectUnscopedHistorySql =>
        "SELECT COUNT(*) = 0 FROM pragma_table_info('__zaorm_migrations') WHERE name = 'source'";

    /// <inheritdoc />
    public IReadOnlyList<string> UpgradeUnscopedHistorySql => UpgradeStatements;

    /// <inheritdoc />
    public Task AcquireLockAsync(IAsyncDbConnection connection, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task ReleaseLockAsync(IAsyncDbConnection connection, CancellationToken ct) => Task.CompletedTask;
}
