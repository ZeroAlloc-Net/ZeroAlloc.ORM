using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.Migrations;

/// <summary>Coverage for <see cref="EfCoreBaseline"/> against a real Sqlite in-memory database.</summary>
public class EfCoreBaselineSqliteTests
{
    private const string First = "20260209134036_AddMissingTaskColumns";
    private const string Second = "20261014090300_AddNodeSelectionUniqueIndex";

    [Fact]
    public async Task No_ef_history_returns_RunAll()
    {
        await using var fixture = await NewFixtureAsync().ConfigureAwait(false);

        var decision = await EfCoreBaseline.LatestMigrationIs(Second, SqlDialect.Sqlite)
            .DecideAsync(fixture.Connection, CancellationToken.None).ConfigureAwait(false);

        decision.Should().Be(BaselineDecision.RunAll);
    }

    [Fact]
    public async Task Latest_id_matches_returns_MarkApplied()
    {
        await using var fixture = await NewFixtureAsync().ConfigureAwait(false);
        await CreateEfHistoryAsync(fixture, First, Second).ConfigureAwait(false);

        var decision = await EfCoreBaseline.LatestMigrationIs(Second, SqlDialect.Sqlite)
            .DecideAsync(fixture.Connection, CancellationToken.None).ConfigureAwait(false);

        decision.Should().Be(BaselineDecision.MarkApplied);
    }

    [Fact]
    public async Task Older_head_throws_naming_both_ids()
    {
        await using var fixture = await NewFixtureAsync().ConfigureAwait(false);
        await CreateEfHistoryAsync(fixture, First).ConfigureAwait(false);

        var act = () => EfCoreBaseline.LatestMigrationIs(Second, SqlDialect.Sqlite)
            .DecideAsync(fixture.Connection, CancellationToken.None);

        var message = (await act.Should().ThrowAsync<ZeroAllocOrmMigrationConflictException>().ConfigureAwait(false))
            .Which.Message;
        message.Should().Contain(First).And.Contain(Second);
    }

    [Fact]
    public async Task Newer_head_throws()
    {
        await using var fixture = await NewFixtureAsync().ConfigureAwait(false);
        await CreateEfHistoryAsync(fixture, First, Second).ConfigureAwait(false);

        var act = () => EfCoreBaseline.LatestMigrationIs(First, SqlDialect.Sqlite)
            .DecideAsync(fixture.Connection, CancellationToken.None);

        await act.Should().ThrowAsync<ZeroAllocOrmMigrationConflictException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task Empty_ef_history_throws()
    {
        await using var fixture = await NewFixtureAsync().ConfigureAwait(false);
        await CreateEfHistoryAsync(fixture).ConfigureAwait(false);

        var act = () => EfCoreBaseline.LatestMigrationIs(Second, SqlDialect.Sqlite)
            .DecideAsync(fixture.Connection, CancellationToken.None);

        (await act.Should().ThrowAsync<ZeroAllocOrmMigrationConflictException>().ConfigureAwait(false))
            .Which.Message.Should().Contain("no rows").And.Contain(Second);
    }

    [Fact]
    public async Task End_to_end_existing_db_marks_baseline_and_applies_later()
    {
        await using var fixture = await NewFixtureAsync().ConfigureAwait(false);
        await CreateEfHistoryAsync(fixture, First, Second).ConfigureAwait(false);
        await ExecuteAsync(fixture, "CREATE TABLE t1 (id INTEGER PRIMARY KEY)").ConfigureAwait(false);
        await ExecuteAsync(fixture, "CREATE TABLE t2 (id INTEGER PRIMARY KEY)").ConfigureAwait(false);
        var source = new ListMigrationSource(
        [
            new Migration(1, "one", "CREATE TABLE t1 (id INTEGER PRIMARY KEY)"),
            new Migration(2, "two", "CREATE TABLE t2 (id INTEGER PRIMARY KEY)"),
            new Migration(3, "three", "CREATE TABLE t3 (id INTEGER PRIMARY KEY)"),
        ]);

        var runner = new MigrationRunner(
            fixture.Connection, source, new SqliteMigrationDialect(),
            new MigrationBaseline(2, EfCoreBaseline.LatestMigrationIs(Second, SqlDialect.Sqlite)));
        var applied = await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);

        applied.Select(m => m.Version).Should().Equal(3);
        (await ScalarAsync(fixture, "SELECT COUNT(*) FROM __EFMigrationsHistory").ConfigureAwait(false)).Should().Be(2L);
        (await ScalarAsync(fixture, "SELECT COUNT(*) FROM __zaorm_migrations").ConfigureAwait(false)).Should().Be(3L);
    }

    [Fact]
    public void Blank_id_throws()
    {
        var act = () => EfCoreBaseline.LatestMigrationIs(" ", SqlDialect.Sqlite);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Unsupported_dialect_throws()
    {
        var act = () => EfCoreBaseline.LatestMigrationIs(Second, SqlDialect.MySql);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static async Task<SqliteFixture> NewFixtureAsync()
    {
        var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);
        return fixture;
    }

    private static async Task CreateEfHistoryAsync(SqliteFixture fixture, params string[] ids)
    {
        await ExecuteAsync(fixture,
            "CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" TEXT PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL)")
            .ConfigureAwait(false);
        foreach (var id in ids)
        {
            await ExecuteAsync(fixture,
                $"INSERT INTO \"__EFMigrationsHistory\" VALUES ('{id}', '9.0.0')").ConfigureAwait(false);
        }
    }

    private static async Task ExecuteAsync(SqliteFixture fixture, string sql)
    {
        var cmd = fixture.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<object?> ScalarAsync(SqliteFixture fixture, string sql)
    {
        var cmd = fixture.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            return await cmd.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private sealed class ListMigrationSource(IReadOnlyList<Migration> migrations) : IMigrationSource
    {
        public string Name => "ef-baseline-tests";
        public IReadOnlyList<Migration> GetMigrations() => migrations;
    }
}
