using System.Data.Async;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.Migrations;

// #306 — the runner's checks on the source name, and a dialect that does not scope.
public sealed class MigrationRunnerSourceNameTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Runner_rejects_an_empty_source_name(string name)
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);

            var ex = Assert.Throws<ArgumentException>(
                () => new MigrationRunner(fx.Connection, new NamedSource(name), new SqliteMigrationDialect()));
            Assert.Equal("source", ex.ParamName);
        }
    }

    [Fact]
    public async Task Runner_accepts_a_source_name_up_to_the_limit()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);

            _ = new MigrationRunner(
                fx.Connection, new NamedSource(new string('x', MigrationRunner.MaxSourceNameLength)), new SqliteMigrationDialect());
            var ex = Assert.Throws<ArgumentException>(() => new MigrationRunner(
                fx.Connection, new NamedSource(new string('x', MigrationRunner.MaxSourceNameLength + 1)), new SqliteMigrationDialect()));
            Assert.Equal("source", ex.ParamName);
        }
    }

    [Fact]
    public async Task Dialect_that_does_not_scope_keeps_one_version_sequence()
    {
        // A custom dialect written against IMigrationDialect alone works as before #306.
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            var dialect = new UnscopedSqliteDialect();

            await new MigrationRunner(fx.Connection, new NamedSource("LibraryA",
                new Migration(1, "create_a", "CREATE TABLE a_table (id INT)")), dialect).RunAsync().ConfigureAwait(false);

            await Assert.ThrowsAsync<ZeroAllocOrmMigrationConflictException>(
                () => new MigrationRunner(fx.Connection, new NamedSource("LibraryB",
                    new Migration(1, "create_b", "CREATE TABLE b_table (id INT)")), dialect).RunAsync()).ConfigureAwait(false);
        }
    }

    private sealed class NamedSource(string name, params Migration[] migrations) : IMigrationSource
    {
        public string Name => name;

        public IReadOnlyList<Migration> GetMigrations() => migrations;
    }

    private sealed class UnscopedSqliteDialect : IMigrationDialect
    {
        public string CreateHistoryTableSql =>
            "CREATE TABLE IF NOT EXISTS __zaorm_migrations (version INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_at TEXT NOT NULL)";

        public string SelectAppliedVersionsSql => "SELECT version, name FROM __zaorm_migrations ORDER BY version";

        public string InsertAppliedVersionSql =>
            "INSERT INTO __zaorm_migrations (version, name, applied_at) VALUES (@version, @name, @applied_at)";

        public Task AcquireLockAsync(IAsyncDbConnection connection, CancellationToken ct) => Task.CompletedTask;

        public Task ReleaseLockAsync(IAsyncDbConnection connection, CancellationToken ct) => Task.CompletedTask;
    }
}
