using System.Data.Async;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.ORM.Migrations;

/// <summary>
/// v1.1 — Provider-specific SQL templates and locking strategy used by
/// <see cref="MigrationRunner"/>. The interface stays small on purpose: the
/// runner orchestrates the apply loop (history table bookstrapping, pending
/// filtering, per-migration transaction); the dialect supplies only the
/// per-provider strings + lock semantics.
/// </summary>
public interface IMigrationDialect
{
    /// <summary>
    /// <c>CREATE TABLE IF NOT EXISTS &lt;history&gt;(...)</c> idempotent DDL used
    /// by the runner to bootstrap the migration history table on first run.
    /// </summary>
    string CreateHistoryTableSql { get; }

    /// <summary>
    /// <c>SELECT version FROM &lt;history&gt; ORDER BY version</c> — the runner
    /// turns the result into a <see cref="System.Collections.Generic.HashSet{T}"/>
    /// of applied versions to filter pending migrations against.
    /// </summary>
    string SelectAppliedVersionsSql { get; }

    /// <summary>
    /// <c>INSERT INTO &lt;history&gt; (version, name, applied_at) VALUES (@version, @name, @applied_at)</c>
    /// — the runner binds three parameters per applied migration.
    /// </summary>
    string InsertAppliedVersionSql { get; }

    /// <summary>
    /// Acquire the provider's apply-lock (e.g. Postgres <c>pg_advisory_lock</c>);
    /// no-op on providers without one (Sqlite's single-writer model serializes
    /// concurrent apply attempts via the WAL / journal). MUST be paired with a
    /// matching <see cref="ReleaseLockAsync"/> in a try/finally.
    /// </summary>
    Task AcquireLockAsync(IAsyncDbConnection connection, CancellationToken ct);

    /// <summary>
    /// Release the apply-lock acquired by <see cref="AcquireLockAsync"/>. Idempotent
    /// when paired with a try/finally — the runner calls this in the finally even
    /// when acquisition succeeded but a downstream step threw.
    /// </summary>
    Task ReleaseLockAsync(IAsyncDbConnection connection, CancellationToken ct);
}
