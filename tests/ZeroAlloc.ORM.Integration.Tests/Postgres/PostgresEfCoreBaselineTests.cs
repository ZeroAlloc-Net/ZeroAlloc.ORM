using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

/// <summary>
/// End-to-end coverage for adopting an EF Core managed PostgreSQL database through
/// <see cref="EfCoreBaseline"/> and <see cref="MigrationBaseline"/>.
/// </summary>
[Trait("Provider", "Postgres")]
public class PostgresEfCoreBaselineTests
{
    private const string EfHead = "20261014090300_AddNodeSelectionUniqueIndex";

    private static readonly Migration[] Daedalus =
    [
        new Migration(1, "create_nodes", "CREATE TABLE nodes (id INTEGER PRIMARY KEY, name TEXT NOT NULL)"),
        new Migration(2, "create_tasks", "CREATE TABLE tasks (id INTEGER PRIMARY KEY, node_id INTEGER NOT NULL)"),
        new Migration(3, "add_tasks_note", "ALTER TABLE tasks ADD COLUMN note TEXT"),
    ];

    [Fact]
    public async Task Existing_ef_database_is_adopted_in_place()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await SeedEfDatabaseAsync(fixture).ConfigureAwait(false);
        await ExecuteAsync(fixture, "INSERT INTO nodes (id, name) VALUES (7, 'seeded')").ConfigureAwait(false);

        var applied = await RunDaedalusAsync(fixture).ConfigureAwait(false);

        applied.Select(m => m.Version).Should().Equal(3);
        (await ScalarAsync(fixture, "SELECT name FROM nodes WHERE id = 7").ConfigureAwait(false)).Should().Be("seeded");
        (await VersionsAsync(fixture, "daedalus").ConfigureAwait(false)).Should().Equal(1, 2, 3);
        (await ScalarAsync(fixture, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"").ConfigureAwait(false)).Should().Be(1L);
    }

    [Fact]
    public async Task Fresh_database_runs_all()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);

        var applied = await RunDaedalusAsync(fixture).ConfigureAwait(false);

        applied.Select(m => m.Version).Should().Equal(1, 2, 3);
        (await ScalarAsync(fixture, "SELECT COUNT(*) FROM nodes").ConfigureAwait(false)).Should().Be(0L);
    }

    [Fact]
    public async Task Baseline_source_after_another_source_marks_only_its_own_rows()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await SeedEfDatabaseAsync(fixture).ConfigureAwait(false);
        var other = new ListMigrationSource("other",
        [
            new Migration(1, "other_one", "CREATE TABLE other_a (id INTEGER PRIMARY KEY)"),
            new Migration(2, "other_two", "CREATE TABLE other_b (id INTEGER PRIMARY KEY)"),
        ]);
        await new MigrationRunner(fixture.Connection, other, new PostgresMigrationDialect())
            .RunAsync(CancellationToken.None).ConfigureAwait(false);

        var applied = await RunDaedalusAsync(fixture).ConfigureAwait(false);

        applied.Select(m => m.Version).Should().Equal(3);
        (await VersionsAsync(fixture, "other").ConfigureAwait(false)).Should().Equal(1, 2);
        (await VersionsAsync(fixture, "daedalus").ConfigureAwait(false)).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Pre_scoping_history_with_baseline_source_first_throws()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await SeedEfDatabaseAsync(fixture).ConfigureAwait(false);
        await ExecuteAsync(fixture,
            "CREATE TABLE __zaorm_migrations (version INTEGER PRIMARY KEY, name TEXT NOT NULL, "
            + "applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW())").ConfigureAwait(false);
        await ExecuteAsync(fixture,
            "INSERT INTO __zaorm_migrations (version, name) VALUES (1, 'other_one')").ConfigureAwait(false);

        var act = () => RunDaedalusAsync(fixture);

        await act.Should().ThrowAsync<ZeroAllocOrmMigrationConflictException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task Ef_history_in_another_schema_is_treated_as_absent()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await ExecuteAsync(fixture, "CREATE SCHEMA other").ConfigureAwait(false);
        await ExecuteAsync(fixture,
            "CREATE TABLE other.\"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, "
            + "\"ProductVersion\" varchar(32) NOT NULL)").ConfigureAwait(false);
        await ExecuteAsync(fixture,
            $"INSERT INTO other.\"__EFMigrationsHistory\" VALUES ('{EfHead}', '9.0.0')").ConfigureAwait(false);

        var decision = await EfCoreBaseline.LatestMigrationIs(EfHead, SqlDialect.PostgreSql)
            .DecideAsync(fixture.Connection, CancellationToken.None).ConfigureAwait(false);

        decision.Should().Be(BaselineDecision.RunAll);
    }

    private static Task<IReadOnlyList<Migration>> RunDaedalusAsync(PostgresFixture fixture)
        => new MigrationRunner(
            fixture.Connection,
            new ListMigrationSource("daedalus", Daedalus),
            new PostgresMigrationDialect(),
            new MigrationBaseline(2, EfCoreBaseline.LatestMigrationIs(EfHead, SqlDialect.PostgreSql)))
            .RunAsync(CancellationToken.None);

    // An EF-managed database: schema for migrations 1 and 2 plus the EF history table at EfHead.
    private static async Task SeedEfDatabaseAsync(PostgresFixture fixture)
    {
        await ExecuteAsync(fixture, Daedalus[0].Sql).ConfigureAwait(false);
        await ExecuteAsync(fixture, Daedalus[1].Sql).ConfigureAwait(false);
        await ExecuteAsync(fixture,
            "CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, "
            + "\"ProductVersion\" varchar(32) NOT NULL)").ConfigureAwait(false);
        await ExecuteAsync(fixture,
            $"INSERT INTO \"__EFMigrationsHistory\" VALUES ('{EfHead}', '9.0.0')").ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(PostgresFixture fixture, string sql)
    {
        var cmd = fixture.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<object?> ScalarAsync(PostgresFixture fixture, string sql)
    {
        var cmd = fixture.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            return await cmd.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<List<int>> VersionsAsync(PostgresFixture fixture, string source)
    {
        var list = new List<int>();
        var cmd = fixture.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT version FROM __zaorm_migrations WHERE source = @source ORDER BY version";
            var p = cmd.CreateParameter();
            p.ParameterName = "@source";
            p.Value = source;
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

    private sealed class ListMigrationSource(string name, IReadOnlyList<Migration> migrations) : IMigrationSource
    {
        public string Name => name;
        public IReadOnlyList<Migration> GetMigrations() => migrations;
    }
}
