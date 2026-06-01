using System.Data.Async;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.ORM.Migrations;

/// <summary>
/// v1.1 — <see cref="IMigrationDialect"/> implementation for Sqlite (via
/// Microsoft.Data.Sqlite). The history table uses Sqlite type affinities:
/// <c>INTEGER</c> primary key for <c>version</c>, <c>TEXT NOT NULL</c> for
/// <c>name</c>, and ISO-8601 <c>TEXT NOT NULL</c> for <c>applied_at</c>
/// (Sqlite stores timestamps as text by convention; the runner writes them
/// via <c>DateTime.UtcNow.ToString("o")</c>).
///
/// <para>
/// Lock semantics are no-ops: Sqlite serializes writers natively (BEGIN
/// EXCLUSIVE / journal / WAL), so the per-migration transaction inside
/// <see cref="MigrationRunner"/> is sufficient for atomicity. Postgres
/// (Phase B) will switch to <c>pg_advisory_lock</c>.
/// </para>
/// </summary>
public sealed class SqliteMigrationDialect : IMigrationDialect
{
    /// <inheritdoc />
    public string CreateHistoryTableSql =>
        "CREATE TABLE IF NOT EXISTS __zaorm_migrations (" +
        "version INTEGER PRIMARY KEY," +
        "name TEXT NOT NULL," +
        "applied_at TEXT NOT NULL)";

    /// <inheritdoc />
    public string SelectAppliedVersionsSql => "SELECT version FROM __zaorm_migrations ORDER BY version";

    /// <inheritdoc />
    public string InsertAppliedVersionSql =>
        "INSERT INTO __zaorm_migrations (version, name, applied_at) VALUES (@version, @name, @applied_at)";

    /// <inheritdoc />
    public Task AcquireLockAsync(IAsyncDbConnection connection, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task ReleaseLockAsync(IAsyncDbConnection connection, CancellationToken ct) => Task.CompletedTask;
}
