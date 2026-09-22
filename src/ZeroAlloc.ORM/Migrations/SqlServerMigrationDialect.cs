using System.Data.Async;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.ORM.Migrations;

/// <summary>
/// <see cref="IMigrationDialect"/> for Microsoft SQL Server 2012 and newer.
/// </summary>
/// <remarks>
/// <para>
/// Lock semantics use <c>sp_getapplock</c> with <c>@LockOwner = 'Session'</c>,
/// the closest equivalent to Postgres' <c>pg_advisory_lock</c>: the lock is held
/// by the connection rather than a transaction, so it survives the per-migration
/// transactions the runner commits individually, and is released explicitly.
/// </para>
/// <para>
/// A session-scoped lock is also released automatically if the connection drops,
/// which matters for the case a migration run is killed mid-apply: the next run
/// acquires cleanly rather than blocking on a lock nobody holds.
/// </para>
/// <para>
/// <c>CREATE TABLE IF NOT EXISTS</c> does not exist here, so the history table is
/// created under an <c>OBJECT_ID</c> guard, which is the portable-across-versions
/// idiom.
/// </para>
/// </remarks>
public sealed class SqlServerMigrationDialect : IMigrationDialect
{
    /// <summary>
    /// Default resource name for <c>sp_getapplock</c>. Distinct from the Postgres
    /// key type because SQL Server identifies application locks by name.
    /// </summary>
    public const string DefaultLockName = "ZAORM_MIGRATIONS";

    private readonly string _lockName;

    /// <summary>
    /// Creates the dialect.
    /// </summary>
    /// <param name="lockName">
    /// Application-lock resource name. Override only to isolate two independent
    /// migration sets running against the same database.
    /// </param>
    public SqlServerMigrationDialect(string lockName = DefaultLockName)
    {
        _lockName = lockName ?? throw new System.ArgumentNullException(nameof(lockName));
    }

    /// <inheritdoc />
    public string CreateHistoryTableSql =>
        "IF OBJECT_ID(N'__zaorm_migrations', N'U') IS NULL " +
        "CREATE TABLE __zaorm_migrations (" +
        "version INT NOT NULL PRIMARY KEY, " +
        "name NVARCHAR(400) NOT NULL, " +
        "applied_at DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET())";

    /// <inheritdoc />
    public string SelectAppliedVersionsSql => "SELECT version FROM __zaorm_migrations ORDER BY version";

    /// <inheritdoc />
    public string InsertAppliedVersionSql =>
        "INSERT INTO __zaorm_migrations (version, name, applied_at) VALUES (@version, @name, @applied_at)";

    /// <inheritdoc />
    public async Task AcquireLockAsync(IAsyncDbConnection connection, CancellationToken ct)
    {
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            // Session-scoped so the lock outlives each migration's own transaction.
            // A negative return means the request failed rather than timed out;
            // -1 specifically is timeout, which with @LockTimeout = -1 cannot occur.
            cmd.CommandText =
                "DECLARE @rc INT; " +
                "EXEC @rc = sp_getapplock @Resource = @lockName, @LockMode = 'Exclusive', " +
                "@LockOwner = 'Session', @LockTimeout = -1; " +
                "IF @rc < 0 THROW 50000, 'Could not acquire the ZeroAlloc.ORM migration lock.', 1;";
            AddLockName(cmd);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task ReleaseLockAsync(IAsyncDbConnection connection, CancellationToken ct)
    {
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            // Guarded by APPLOCK_MODE because sp_releaseapplock THROWS when the
            // lock is not held -- "Cannot release the application lock ... because
            // it is not currently held" -- rather than returning a negative code as
            // sp_getapplock does. The interface requires release to be idempotent,
            // since the runner calls it from a finally block that also runs when
            // acquisition itself failed.
            //
            // Checking the mode rather than swallowing the exception keeps genuine
            // release failures visible.
            cmd.CommandText =
                "IF APPLOCK_MODE('public', @lockName, 'Session') <> 'NoLock' " +
                "EXEC sp_releaseapplock @Resource = @lockName, @LockOwner = 'Session';";
            AddLockName(cmd);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private void AddLockName(IAsyncDbCommand cmd)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = "@lockName";
        p.Value = _lockName;
        cmd.Parameters.Add(p);
    }
}
