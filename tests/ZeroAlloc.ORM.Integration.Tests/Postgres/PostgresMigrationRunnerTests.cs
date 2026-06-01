using System;
using System.Collections.Generic;
using System.Data.Async;
using System.Data.Async.Adapters;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Npgsql;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

/// <summary>
/// v1.1 Phase B.2 — end-to-end coverage for <see cref="MigrationRunner"/> against
/// a real Postgres database. Mirrors the four Sqlite scenarios and adds two
/// Postgres-specific cases that exercise <c>pg_advisory_lock</c>:
///   * <see cref="Two_parallel_runners_serialize_via_advisory_lock"/> — two
///     runners on independent sessions race against an empty database; the
///     advisory lock serializes their CREATE / INSERT path so exactly one
///     migration row lands in the history table.
///   * <see cref="Lock_released_on_exception"/> — when a migration body throws,
///     the runner's finally block releases the lock so a subsequent runner
///     doesn't block forever on a stale acquisition.
/// </summary>
[Trait("Provider", "Postgres")]
public class PostgresMigrationRunnerTests
{
    [Fact]
    public async Task Empty_db_applies_all_migrations()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);

        var source = new ListMigrationSource(new[]
        {
            new Migration(1, "create_users", "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL)"),
            new Migration(2, "create_orders", "CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL)"),
            new Migration(3, "add_user_email", "ALTER TABLE users ADD COLUMN email TEXT"),
        });

        var runner = new MigrationRunner(fixture.Connection, source, new PostgresMigrationDialect());
        var applied = await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);

        applied.Should().HaveCount(3);
        applied.Select(m => m.Version).Should().Equal(1, 2, 3);
        applied.Should().AllSatisfy(m => m.AppliedAt.Should().NotBeNull());

        var versions = await ReadAppliedVersionsAsync(fixture.Connection).ConfigureAwait(false);
        versions.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Already_applied_migration_is_skipped()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);

        var migrations = new[]
        {
            new Migration(1, "create_users", "CREATE TABLE users (id INTEGER PRIMARY KEY)"),
            new Migration(2, "create_orders", "CREATE TABLE orders (id INTEGER PRIMARY KEY)"),
        };
        var dialect = new PostgresMigrationDialect();
        var source = new ListMigrationSource(migrations);

        var first = await new MigrationRunner(fixture.Connection, source, dialect)
            .RunAsync(CancellationToken.None).ConfigureAwait(false);
        first.Should().HaveCount(2);

        // Re-run on the same DB — both versions are already in history, so no
        // CREATE TABLE statements re-execute (they'd otherwise throw on
        // duplicate object).
        var second = await new MigrationRunner(fixture.Connection, source, dialect)
            .RunAsync(CancellationToken.None).ConfigureAwait(false);
        second.Should().BeEmpty();

        var versions = await ReadAppliedVersionsAsync(fixture.Connection).ConfigureAwait(false);
        versions.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Mid_apply_failure_rolls_back_failing_migration_only()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);

        var source = new ListMigrationSource(new[]
        {
            new Migration(1, "create_users", "CREATE TABLE users (id INTEGER PRIMARY KEY)"),
            new Migration(2, "broken", "THIS IS NOT VALID SQL"),
            new Migration(3, "create_orders", "CREATE TABLE orders (id INTEGER PRIMARY KEY)"),
        });

        var runner = new MigrationRunner(fixture.Connection, source, new PostgresMigrationDialect());

        var act = async () => await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);
        await act.Should().ThrowAsync<Exception>().ConfigureAwait(false);

        var versions = await ReadAppliedVersionsAsync(fixture.Connection).ConfigureAwait(false);
        versions.Should().Equal(1);

        (await TableExistsAsync(fixture.Connection, "users").ConfigureAwait(false)).Should().BeTrue();
        (await TableExistsAsync(fixture.Connection, "orders").ConfigureAwait(false)).Should().BeFalse();
    }

    [Fact]
    public async Task Out_of_order_versions_in_source_are_applied_in_version_order()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);

        // Source lists [3, 1, 2]; runner must re-sort to apply [1, 2, 3]. If it
        // ran source-order, migration 3's ALTER would fail because `users`
        // wouldn't yet exist.
        var source = new ListMigrationSource(new[]
        {
            new Migration(3, "add_email", "ALTER TABLE users ADD COLUMN email TEXT"),
            new Migration(1, "create_users", "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL)"),
            new Migration(2, "add_name_index", "CREATE INDEX users_name_idx ON users(name)"),
        });

        var runner = new MigrationRunner(fixture.Connection, source, new PostgresMigrationDialect());
        var applied = await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);

        applied.Select(m => m.Version).Should().Equal(1, 2, 3);

        var versions = await ReadAppliedVersionsAsync(fixture.Connection).ConfigureAwait(false);
        versions.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Two_parallel_runners_serialize_via_advisory_lock()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);

        // Second session — pg_advisory_lock is session-scoped, so distinct
        // connections are the only way to observe contention.
        var raw2 = new NpgsqlConnection(fixture.ConnectionString);
        await using (raw2.ConfigureAwait(false))
        {
            var conn2 = raw2.AsAsync();
            await using (conn2.ConfigureAwait(false))
            {
                await conn2.OpenAsync(CancellationToken.None).ConfigureAwait(false);

                // Single migration — both runners discover it; the lock decides
                // which runner gets to insert it. The loser sees the history
                // row on its read of applied versions and skips the apply step.
                var migrations = new[]
                {
                    new Migration(1, "create_users", "CREATE TABLE users (id INTEGER PRIMARY KEY)"),
                };
                var source1 = new ListMigrationSource(migrations);
                var source2 = new ListMigrationSource(migrations);

                var runner1 = new MigrationRunner(fixture.Connection, source1, new PostgresMigrationDialect());
                var runner2 = new MigrationRunner(conn2, source2, new PostgresMigrationDialect());

                // Fire both at once.
                var task1 = runner1.RunAsync(CancellationToken.None);
                var task2 = runner2.RunAsync(CancellationToken.None);

                var results = await Task.WhenAll(task1, task2).ConfigureAwait(false);

                // Exactly one runner applied the migration; the other observed
                // the prior insert and returned an empty list.
                var totalApplied = results.Sum(r => r.Count);
                totalApplied.Should().Be(1, "advisory lock serializes the apply path so the second runner sees the first's commit");

                var versions = await ReadAppliedVersionsAsync(fixture.Connection).ConfigureAwait(false);
                versions.Should().Equal(1);
            }
        }
    }

    [Fact]
    public async Task Lock_released_on_exception()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);

        // First runner throws on a broken migration. MigrationRunner.RunAsync's
        // finally MUST call ReleaseLockAsync so the next runner doesn't block.
        var bad = new ListMigrationSource(new[]
        {
            new Migration(1, "broken", "THIS IS NOT VALID SQL"),
        });

        var firstRunner = new MigrationRunner(fixture.Connection, bad, new PostgresMigrationDialect());
        var act = async () => await firstRunner.RunAsync(CancellationToken.None).ConfigureAwait(false);
        await act.Should().ThrowAsync<Exception>().ConfigureAwait(false);

        // From a SEPARATE session, attempt pg_try_advisory_lock with the same
        // key. If the previous runner failed to release, this returns false.
        // Using a separate session because the original session would already
        // own the lock (re-entrant pg_advisory_lock).
        var raw2 = new NpgsqlConnection(fixture.ConnectionString);
        await using (raw2.ConfigureAwait(false))
        {
            var conn2 = raw2.AsAsync();
            await using (conn2.ConfigureAwait(false))
            {
                await conn2.OpenAsync(CancellationToken.None).ConfigureAwait(false);

                var cmd = conn2.CreateCommand();
                await using (cmd.ConfigureAwait(false))
                {
                    cmd.CommandText = "SELECT pg_try_advisory_lock(@key)";
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@key";
                    p.Value = PostgresMigrationDialect.DefaultLockKey;
                    cmd.Parameters.Add(p);

                    var result = await cmd.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
                    var acquired = Convert.ToBoolean(result, CultureInfo.InvariantCulture);
                    acquired.Should().BeTrue("ReleaseLockAsync ran in the finally even though the migration body threw");
                }

                // Tidy: release the lock we just grabbed so the container can
                // shut down without holding a stray advisory lock.
                var releaseCmd = conn2.CreateCommand();
                await using (releaseCmd.ConfigureAwait(false))
                {
                    releaseCmd.CommandText = "SELECT pg_advisory_unlock(@key)";
                    var p = releaseCmd.CreateParameter();
                    p.ParameterName = "@key";
                    p.Value = PostgresMigrationDialect.DefaultLockKey;
                    releaseCmd.Parameters.Add(p);
                    await releaseCmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }

    // --- helpers -----------------------------------------------------------

    private static async Task<List<int>> ReadAppliedVersionsAsync(IAsyncDbConnection conn)
    {
        var list = new List<int>();
        var cmd = conn.CreateCommand();
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

    private static async Task<bool> TableExistsAsync(IAsyncDbConnection conn, string tableName)
    {
        var cmd = conn.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText =
                "SELECT COUNT(*) FROM information_schema.tables " +
                "WHERE table_schema = 'public' AND table_name = @name";
            var p = cmd.CreateParameter();
            p.ParameterName = "@name";
            p.Value = tableName;
            cmd.Parameters.Add(p);
            var result = await cmd.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
        }
    }

    /// <summary>
    /// Test-only in-memory <see cref="IMigrationSource"/> — accepts any ordering
    /// and returns the list verbatim (the runner does its own sort). Mirrors
    /// the ListMigrationSource helper in MigrationRunnerSqliteTests; duplicated
    /// here rather than promoted to a shared test util because the v1.1 plan
    /// scope is small and a shared helper would be premature abstraction.
    /// </summary>
    private sealed class ListMigrationSource : IMigrationSource
    {
        private readonly IReadOnlyList<Migration> _migrations;
        public ListMigrationSource(IEnumerable<Migration> migrations) => _migrations = migrations.ToList();
        public IReadOnlyList<Migration> GetMigrations() => _migrations;
    }
}
