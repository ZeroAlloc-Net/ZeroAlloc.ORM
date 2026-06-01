using System;
using System.Collections.Generic;
using System.Data.Async;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.Migrations;

/// <summary>
/// v1.1 Phase B.1 — sanity checks for the Postgres-flavored dialect: the schema
/// emitted by <see cref="PostgresMigrationDialect.CreateHistoryTableSql"/> must
/// be valid Postgres DDL, idempotent across re-runs (CREATE IF NOT EXISTS), and
/// round-trip an <c>(version, name, applied_at)</c> row via the prepared SELECT
/// / INSERT statements. The advisory-lock acquire/release pair is exercised as
/// a basic sanity check here; cross-runner serialization coverage lives in
/// Phase B.2's <see cref="PostgresMigrationRunnerTests"/>.
/// </summary>
[Trait("Provider", "Postgres")]
public class PostgresMigrationDialectTests
{
    [Fact]
    public async Task CreateHistoryTable_succeeds_on_fresh_db()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        var dialect = new PostgresMigrationDialect();

        await fixture.ExecuteDdlAsync(dialect.CreateHistoryTableSql).ConfigureAwait(false);

        // Read the table back via information_schema to assert the table exists.
        var cmd = fixture.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText =
                "SELECT COUNT(*) FROM information_schema.tables " +
                "WHERE table_schema = 'public' AND table_name = '__zaorm_migrations'";
            var count = await cmd.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
            Convert.ToInt32(count, CultureInfo.InvariantCulture).Should().Be(1);
        }
    }

    [Fact]
    public async Task CreateHistoryTable_is_idempotent()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        var dialect = new PostgresMigrationDialect();

        await fixture.ExecuteDdlAsync(dialect.CreateHistoryTableSql).ConfigureAwait(false);
        // Second run must not throw — CREATE TABLE IF NOT EXISTS is the contract.
        await fixture.ExecuteDdlAsync(dialect.CreateHistoryTableSql).ConfigureAwait(false);
    }

    [Fact]
    public async Task SelectAppliedVersions_on_empty_table_returns_empty()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        var dialect = new PostgresMigrationDialect();
        await fixture.ExecuteDdlAsync(dialect.CreateHistoryTableSql).ConfigureAwait(false);

        var versions = await ReadVersionsAsync(fixture.Connection, dialect).ConfigureAwait(false);

        versions.Should().BeEmpty();
    }

    [Fact]
    public async Task InsertAppliedVersion_then_SelectAppliedVersions_returns_inserted_version()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        var dialect = new PostgresMigrationDialect();
        await fixture.ExecuteDdlAsync(dialect.CreateHistoryTableSql).ConfigureAwait(false);

        await InsertRowAsync(fixture.Connection, dialect, version: 7, name: "lucky_seven").ConfigureAwait(false);
        await InsertRowAsync(fixture.Connection, dialect, version: 8, name: "next_one").ConfigureAwait(false);

        var versions = await ReadVersionsAsync(fixture.Connection, dialect).ConfigureAwait(false);

        versions.Should().Equal(7, 8);
    }

    [Fact]
    public async Task AcquireLock_then_ReleaseLock_round_trips()
    {
        await using var fixture = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        var dialect = new PostgresMigrationDialect();

        // Acquire-then-release on a single session must complete without
        // blocking or throwing — pg_advisory_lock is session-scoped and the
        // unlock returns true when the lock was held.
        await dialect.AcquireLockAsync(fixture.Connection, CancellationToken.None).ConfigureAwait(false);
        await dialect.ReleaseLockAsync(fixture.Connection, CancellationToken.None).ConfigureAwait(false);

        // After release, the same session can re-acquire immediately.
        await dialect.AcquireLockAsync(fixture.Connection, CancellationToken.None).ConfigureAwait(false);
        await dialect.ReleaseLockAsync(fixture.Connection, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task InsertRowAsync(IAsyncDbConnection conn, PostgresMigrationDialect dialect, int version, string name)
    {
        var cmd = conn.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = dialect.InsertAppliedVersionSql;

            var pVer = cmd.CreateParameter();
            pVer.ParameterName = "@version";
            pVer.Value = version;
            cmd.Parameters.Add(pVer);

            var pName = cmd.CreateParameter();
            pName.ParameterName = "@name";
            pName.Value = name;
            cmd.Parameters.Add(pName);

            var pAt = cmd.CreateParameter();
            pAt.ParameterName = "@applied_at";
            pAt.Value = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            cmd.Parameters.Add(pAt);

            await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<List<int>> ReadVersionsAsync(IAsyncDbConnection conn, PostgresMigrationDialect dialect)
    {
        var list = new List<int>();
        var cmd = conn.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = dialect.SelectAppliedVersionsSql;
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
}
