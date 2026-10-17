using System;
using System.Collections.Generic;
using System.Data.Async;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.Migrations;

/// <summary>
/// Coverage for the <see cref="MigrationBaseline"/> option of <see cref="MigrationRunner"/>
/// against a real Sqlite in-memory database.
/// </summary>
public class MigrationRunnerBaselineSqliteTests
{
    private static readonly Migration[] ThreeMigrations =
    [
        new Migration(1, "one", "CREATE TABLE t1 (id INTEGER PRIMARY KEY)"),
        new Migration(2, "two", "CREATE TABLE t2 (id INTEGER PRIMARY KEY)"),
        new Migration(3, "three", "CREATE TABLE t3 (id INTEGER PRIMARY KEY)"),
    ];

    [Fact]
    public async Task MarkApplied_records_through_version_without_running_them()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);
        await ExecuteAsync(fixture, "CREATE TABLE t1 (id INTEGER PRIMARY KEY)").ConfigureAwait(false);
        await ExecuteAsync(fixture, "CREATE TABLE t2 (id INTEGER PRIMARY KEY)").ConfigureAwait(false);

        var source = new ListMigrationSource(ThreeMigrations);
        var runner = new MigrationRunner(
            fixture.Connection, source, new SqliteMigrationDialect(),
            new MigrationBaseline(2, new FixedCheck(BaselineDecision.MarkApplied)));
        var applied = await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);

        applied.Select(m => m.Version).Should().Equal(3);
        (await ReadVersionsAsync(fixture, source.Name).ConfigureAwait(false)).Should().Equal(1, 2, 3);
        (await TableExistsAsync(fixture, "t3").ConfigureAwait(false)).Should().BeTrue();
        (await ReadNamesAsync(fixture, source.Name).ConfigureAwait(false)).Take(2).Should().Equal("one", "two");
    }

    [Fact]
    public async Task Baseline_insert_failure_rolls_back_and_writes_nothing()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);
        var source = new ListMigrationSource(ThreeMigrations);

        var runner = new MigrationRunner(
            fixture.Connection, source, new FailingSecondInsertDialect(),
            new MigrationBaseline(2, new FixedCheck(BaselineDecision.MarkApplied)));
        var act = () => runner.RunAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().ConfigureAwait(false);
        (await ReadVersionsAsync(fixture, source.Name).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAll_runs_every_migration()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);

        var runner = new MigrationRunner(
            fixture.Connection, new ListMigrationSource(ThreeMigrations), new SqliteMigrationDialect(),
            new MigrationBaseline(2, new FixedCheck(BaselineDecision.RunAll)));
        var applied = await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);

        applied.Select(m => m.Version).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Second_run_is_a_noop_and_does_not_call_the_check()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);
        await ExecuteAsync(fixture, "CREATE TABLE t1 (id INTEGER PRIMARY KEY)").ConfigureAwait(false);
        await ExecuteAsync(fixture, "CREATE TABLE t2 (id INTEGER PRIMARY KEY)").ConfigureAwait(false);

        var check = new FixedCheck(BaselineDecision.MarkApplied);
        var baseline = new MigrationBaseline(2, check);
        var source = new ListMigrationSource(ThreeMigrations);

        await new MigrationRunner(fixture.Connection, source, new SqliteMigrationDialect(), baseline)
            .RunAsync(CancellationToken.None).ConfigureAwait(false);
        var second = await new MigrationRunner(fixture.Connection, source, new SqliteMigrationDialect(), baseline)
            .RunAsync(CancellationToken.None).ConfigureAwait(false);

        second.Should().BeEmpty();
        check.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Check_is_skipped_when_source_already_has_history()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);
        var source = new ListMigrationSource(ThreeMigrations);

        await new MigrationRunner(fixture.Connection, source, new SqliteMigrationDialect())
            .RunAsync(CancellationToken.None).ConfigureAwait(false);

        var runner = new MigrationRunner(
            fixture.Connection, source, new SqliteMigrationDialect(),
            new MigrationBaseline(2, new ThrowingCheck()));
        var applied = await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);

        applied.Should().BeEmpty();
    }

    [Fact]
    public async Task Check_exception_propagates_and_writes_nothing()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);
        var source = new ListMigrationSource(ThreeMigrations);

        var runner = new MigrationRunner(
            fixture.Connection, source, new SqliteMigrationDialect(),
            new MigrationBaseline(2, new ThrowingCheck()));
        var act = () => runner.RunAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ZeroAllocOrmMigrationConflictException>().ConfigureAwait(false);
        (await ReadVersionsAsync(fixture, source.Name).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Fact]
    public async Task Baseline_version_missing_from_source_throws_before_writing()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);
        var source = new ListMigrationSource(ThreeMigrations);
        var check = new FixedCheck(BaselineDecision.MarkApplied);

        var runner = new MigrationRunner(
            fixture.Connection, source, new SqliteMigrationDialect(), new MigrationBaseline(5, check));
        var act = () => runner.RunAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<ZeroAllocOrmMigrationConflictException>().ConfigureAwait(false))
            .Which.Message.Should().Contain("5");
        check.Calls.Should().Be(0);
        (await ReadVersionsAsync(fixture, source.Name).ConfigureAwait(false)).Should().BeEmpty();
    }

    // --- helpers -----------------------------------------------------------

    private static async Task ExecuteAsync(SqliteFixture fixture, string sql)
    {
        var cmd = fixture.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<List<int>> ReadVersionsAsync(SqliteFixture fixture, string sourceName)
    {
        var list = new List<int>();
        var cmd = fixture.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT version FROM __zaorm_migrations WHERE source = @source ORDER BY version";
            var p = cmd.CreateParameter();
            p.ParameterName = "@source";
            p.Value = sourceName;
            cmd.Parameters.Add(p);
            var reader = await cmd.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
            await using (((IAsyncDisposable)reader).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    list.Add(reader.GetInt32(0));
                }
            }
        }
        return list;
    }

    private static async Task<List<string>> ReadNamesAsync(SqliteFixture fixture, string sourceName)
    {
        var list = new List<string>();
        var cmd = fixture.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT name FROM __zaorm_migrations WHERE source = @source ORDER BY version";
            var p = cmd.CreateParameter();
            p.ParameterName = "@source";
            p.Value = sourceName;
            cmd.Parameters.Add(p);
            var reader = await cmd.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
            await using (((IAsyncDisposable)reader).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    list.Add(reader.GetString(0));
                }
            }
        }
        return list;
    }

    private static async Task<bool> TableExistsAsync(SqliteFixture fixture, string tableName)
    {
        var cmd = fixture.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
            var p = cmd.CreateParameter();
            p.ParameterName = "@name";
            p.Value = tableName;
            cmd.Parameters.Add(p);
            var result = await cmd.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
            return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) > 0;
        }
    }

    private sealed class FixedCheck : IMigrationBaselineCheck
    {
        private readonly BaselineDecision _decision;
        public FixedCheck(BaselineDecision decision) => _decision = decision;
        public int Calls { get; private set; }

        public Task<BaselineDecision> DecideAsync(IAsyncDbConnection connection, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_decision);
        }
    }

    // Delegates to the Sqlite dialect, but hands out invalid insert SQL from the second read on,
    // so the baseline's second row fails after the first was written.
    private sealed class FailingSecondInsertDialect : IScopedMigrationDialect
    {
        private readonly SqliteMigrationDialect _inner = new();
        private int _reads;

        public string CreateHistoryTableSql => _inner.CreateHistoryTableSql;
        public string SelectAppliedVersionsSql => _inner.SelectAppliedVersionsSql;
        public string InsertAppliedVersionSql
            => ++_reads == 1 ? _inner.InsertAppliedVersionSql : "INSERT INTO no_such_table VALUES (1)";
        public string SelectUnscopedHistorySql => _inner.SelectUnscopedHistorySql;
        public IReadOnlyList<string> UpgradeUnscopedHistorySql => _inner.UpgradeUnscopedHistorySql;
        public Task AcquireLockAsync(IAsyncDbConnection connection, CancellationToken ct)
            => _inner.AcquireLockAsync(connection, ct);
        public Task ReleaseLockAsync(IAsyncDbConnection connection, CancellationToken ct)
            => _inner.ReleaseLockAsync(connection, ct);
    }

    private sealed class ThrowingCheck : IMigrationBaselineCheck
    {
        public Task<BaselineDecision> DecideAsync(IAsyncDbConnection connection, CancellationToken ct)
            => throw new ZeroAllocOrmMigrationConflictException("The database does not match the baseline.");
    }

    private sealed class ListMigrationSource : IMigrationSource
    {
        private readonly IReadOnlyList<Migration> _migrations;
        public ListMigrationSource(IEnumerable<Migration> migrations) => _migrations = migrations.ToList();
        public string Name => "baseline-tests";
        public IReadOnlyList<Migration> GetMigrations() => _migrations;
    }
}
