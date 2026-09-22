using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.SqlServer;

/// <summary>
/// Exercises <see cref="SqlServerMigrationDialect"/> against a real SQL Server.
/// </summary>
/// <remarks>
/// The dialect is nothing but provider-specific SQL — an <c>OBJECT_ID</c> guard
/// instead of <c>CREATE TABLE IF NOT EXISTS</c>, and <c>sp_getapplock</c> instead
/// of <c>pg_advisory_lock</c>. None of that can be verified by compiling, so these
/// run against the engine.
/// </remarks>
public sealed class SqlServerMigrationDialectTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fx = new();

    public ValueTask InitializeAsync() => _fx.InitializeAsync();
    public ValueTask DisposeAsync() => _fx.DisposeAsync();

    private static StaticSource Source(params Migration[] migrations)
        => new(migrations);

    [Fact]
    public async Task Applies_Migrations_And_Records_History()
    {
        var dialect = new SqlServerMigrationDialect();
        var runner = new MigrationRunner(_fx.Connection, Source(
            new Migration(1, "create_widgets",
                "CREATE TABLE Widgets (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(200) NOT NULL);")), dialect);

        var applied = await runner.RunAsync(default);

        Assert.Single(applied);
        Assert.Equal(1, applied[0].Version);
        Assert.True(await TableExistsAsync("Widgets"));
        Assert.True(await TableExistsAsync("__zaorm_migrations"));
    }

    [Fact]
    public async Task Re_Running_Applies_Nothing_Further()
    {
        var dialect = new SqlServerMigrationDialect();
        var source = Source(new Migration(1, "create_widgets",
            "CREATE TABLE Widgets (Id INT NOT NULL PRIMARY KEY);"));

        Assert.Single(await new MigrationRunner(_fx.Connection, source, dialect).RunAsync(default));
        Assert.Empty(await new MigrationRunner(_fx.Connection, source, dialect).RunAsync(default));
    }

    [Fact]
    public async Task Applies_Pending_Migrations_In_Version_Order()
    {
        var dialect = new SqlServerMigrationDialect();
        var runner = new MigrationRunner(_fx.Connection, Source(
            new Migration(2, "add_colour", "ALTER TABLE Widgets ADD Colour NVARCHAR(50) NULL;"),
            new Migration(1, "create_widgets", "CREATE TABLE Widgets (Id INT NOT NULL PRIMARY KEY);")), dialect);

        var applied = await runner.RunAsync(default);

        // Version 2 alters a table version 1 creates, so out-of-order application
        // would fail outright rather than merely produce a surprising order.
        Assert.Equal([1, 2], applied.Select(m => m.Version).ToArray());
    }

    [Fact]
    public async Task History_Table_Creation_Is_Idempotent()
    {
        // OBJECT_ID guard rather than CREATE TABLE IF NOT EXISTS, which SQL Server
        // does not have. Running the statement twice must not throw.
        var dialect = new SqlServerMigrationDialect();
        await ExecuteAsync(dialect.CreateHistoryTableSql);
        await ExecuteAsync(dialect.CreateHistoryTableSql);

        Assert.True(await TableExistsAsync("__zaorm_migrations"));
    }

    [Fact]
    public async Task The_Apply_Lock_Excludes_A_Second_Connection()
    {
        // The point of the lock: two instances starting together must not apply the
        // same migration twice. sp_getapplock with LockOwner = 'Session' is held by
        // the connection, so it survives the per-migration transactions the runner
        // commits individually.
        var dialect = new SqlServerMigrationDialect();
        await dialect.AcquireLockAsync(_fx.Connection, default);

        var other = await _fx.OpenSecondAsync();
        await using (other.ConfigureAwait(false))
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var blocked = dialect.AcquireLockAsync(other, cts.Token);

            var finished = await Task.WhenAny(blocked, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None));
            Assert.NotSame(blocked, finished);   // still waiting — the lock is held

            await dialect.ReleaseLockAsync(_fx.Connection, default);
            await blocked;                        // now it can proceed
            await dialect.ReleaseLockAsync(other, default);
        }
    }

    [Fact]
    public async Task Releasing_A_Lock_We_Do_Not_Hold_Does_Not_Throw()
    {
        // The interface requires release to be idempotent, because the runner calls
        // it from a finally block that also runs when acquisition failed.
        var dialect = new SqlServerMigrationDialect();

        await dialect.ReleaseLockAsync(_fx.Connection, default);
        await dialect.ReleaseLockAsync(_fx.Connection, default);
    }

    private async Task ExecuteAsync(string sql)
    {
        var cmd = _fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(default).ConfigureAwait(false);
        }
    }

    private async Task<bool> TableExistsAsync(string name)
    {
        var cmd = _fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = @n";
            var p = cmd.CreateParameter();
            p.ParameterName = "@n";
            p.Value = name;
            cmd.Parameters.Add(p);
            var scalar = await cmd.ExecuteScalarAsync(default).ConfigureAwait(false);
            return Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture) > 0;
        }
    }

    private sealed class StaticSource(Migration[] migrations) : IMigrationSource
    {
        public IReadOnlyList<Migration> GetMigrations() => migrations;
    }
}
