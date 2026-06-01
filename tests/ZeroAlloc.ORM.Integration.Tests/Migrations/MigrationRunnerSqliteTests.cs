using System;
using System.Collections.Generic;
using System.Data.Async;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.Migrations;

/// <summary>
/// v1.1 Phase A.3 — end-to-end coverage for <see cref="MigrationRunner"/> against
/// a real Sqlite in-memory database. Uses an in-memory <see cref="IMigrationSource"/>
/// (rather than embedded SQL fixtures) so failing-SQL test cases can be expressed
/// inline without polluting the assembly resource manifest.
///
/// Each test exercises:
///   * apply-all (cold start)
///   * skip-already-applied (warm start)
///   * mid-apply failure rolls back the failing migration only, leaves earlier
///     committed migrations intact, and stops the apply loop
///   * out-of-order source ordering still applies in ascending version order
/// </summary>
public class MigrationRunnerSqliteTests
{
    [Fact]
    public async Task Empty_db_applies_all_migrations()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);

        var source = new ListMigrationSource(new[]
        {
            new Migration(1, "create_users", "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL)"),
            new Migration(2, "create_orders", "CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL)"),
            new Migration(3, "add_user_email", "ALTER TABLE users ADD COLUMN email TEXT"),
        });

        var runner = new MigrationRunner(fixture.Connection, source, new SqliteMigrationDialect());
        var applied = await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);

        applied.Should().HaveCount(3);
        applied.Select(m => m.Version).Should().Equal(1, 2, 3);
        applied.Should().AllSatisfy(m => m.AppliedAt.Should().NotBeNull());

        // History table reports all three versions.
        var versions = await ReadAppliedVersionsAsync(fixture).ConfigureAwait(false);
        versions.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Already_applied_migration_is_skipped()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);

        var migrations = new[]
        {
            new Migration(1, "create_users", "CREATE TABLE users (id INTEGER PRIMARY KEY)"),
            new Migration(2, "create_orders", "CREATE TABLE orders (id INTEGER PRIMARY KEY)"),
        };
        var dialect = new SqliteMigrationDialect();
        var source = new ListMigrationSource(migrations);

        // First run applies both.
        var first = await new MigrationRunner(fixture.Connection, source, dialect)
            .RunAsync(CancellationToken.None).ConfigureAwait(false);
        first.Should().HaveCount(2);

        // Second run finds nothing pending and returns an empty list — without
        // re-executing the CREATE TABLE statements (which would otherwise fail
        // with "table already exists").
        var second = await new MigrationRunner(fixture.Connection, source, dialect)
            .RunAsync(CancellationToken.None).ConfigureAwait(false);
        second.Should().BeEmpty();

        // History table still has exactly the two original rows.
        var versions = await ReadAppliedVersionsAsync(fixture).ConfigureAwait(false);
        versions.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Mid_apply_failure_rolls_back_failing_migration_only()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);

        var source = new ListMigrationSource(new[]
        {
            new Migration(1, "create_users", "CREATE TABLE users (id INTEGER PRIMARY KEY)"),
            // Deliberately broken: SYNTAX-ERROR is not a valid Sqlite statement.
            new Migration(2, "broken", "THIS IS NOT VALID SQL"),
            new Migration(3, "create_orders", "CREATE TABLE orders (id INTEGER PRIMARY KEY)"),
        });

        var runner = new MigrationRunner(fixture.Connection, source, new SqliteMigrationDialect());

        // Runner must surface the original provider exception.
        var act = async () => await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);
        await act.Should().ThrowAsync<Exception>().ConfigureAwait(false);

        // History contains only version 1 — migration 2 rolled back, migration 3
        // never attempted.
        var versions = await ReadAppliedVersionsAsync(fixture).ConfigureAwait(false);
        versions.Should().Equal(1);

        // 'users' exists; 'orders' does not.
        (await TableExistsAsync(fixture, "users").ConfigureAwait(false)).Should().BeTrue();
        (await TableExistsAsync(fixture, "orders").ConfigureAwait(false)).Should().BeFalse();
    }

    [Fact]
    public async Task Out_of_order_versions_in_source_are_applied_in_version_order()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);

        // Source returns them in [3, 1, 2] order; the runner must re-sort and
        // execute as [1, 2, 3]. The first migration's DDL creates a table that
        // the third migration's DDL ALTERs — if the runner applied in source
        // order, migration 3 (originally written for table-state-after-1) would
        // fail because the table wouldn't exist yet.
        var source = new ListMigrationSource(new[]
        {
            new Migration(3, "add_email", "ALTER TABLE users ADD COLUMN email TEXT"),
            new Migration(1, "create_users", "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL)"),
            new Migration(2, "add_name_index", "CREATE INDEX users_name_idx ON users(name)"),
        });

        var runner = new MigrationRunner(fixture.Connection, source, new SqliteMigrationDialect());
        var applied = await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);

        applied.Select(m => m.Version).Should().Equal(1, 2, 3);

        var versions = await ReadAppliedVersionsAsync(fixture).ConfigureAwait(false);
        versions.Should().Equal(1, 2, 3);
    }

    // --- helpers -----------------------------------------------------------

    private static async Task<List<int>> ReadAppliedVersionsAsync(SqliteFixture fixture)
    {
        var list = new List<int>();
        var cmd = fixture.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT version FROM __zaorm_migrations ORDER BY version";
            var reader = await cmd.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
            await using (((IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
            {
                while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    list.Add(reader.GetInt32(0));
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

    /// <summary>
    /// Test-only in-memory <see cref="IMigrationSource"/> — accepts any ordering
    /// and returns the list verbatim (the runner does its own sort).
    /// </summary>
    private sealed class ListMigrationSource : IMigrationSource
    {
        private readonly IReadOnlyList<Migration> _migrations;
        public ListMigrationSource(IEnumerable<Migration> migrations) => _migrations = migrations.ToList();
        public IReadOnlyList<Migration> GetMigrations() => _migrations;
    }
}
