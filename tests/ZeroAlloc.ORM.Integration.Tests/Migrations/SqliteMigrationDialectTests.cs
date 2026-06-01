using System.Data.Async;
using FluentAssertions;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.Migrations;

/// <summary>
/// v1.1 Phase A.2 — sanity checks for the Sqlite-flavored dialect: the schema
/// emitted by <see cref="SqliteMigrationDialect.CreateHistoryTableSql"/> must
/// be valid Sqlite DDL, idempotent across re-runs (CREATE IF NOT EXISTS), and
/// round-trip an <c>(version, name, applied_at)</c> row via the prepared
/// SELECT / INSERT statements.
///
/// AcquireLock / ReleaseLock are no-ops on Sqlite (single-writer model serializes
/// concurrent apply attempts via the WAL / journal); the runner-level coverage
/// in A.3 exercises the lock entry/exit dance.
/// </summary>
public class SqliteMigrationDialectTests
{
    [Fact]
    public async Task CreateHistoryTable_succeeds_on_fresh_db()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);
        var dialect = new SqliteMigrationDialect();

        await fixture.ExecuteDdlAsync(dialect.CreateHistoryTableSql).ConfigureAwait(false);

        // Read the table back via sqlite_master to assert the table exists.
        var cmd = fixture.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__zaorm_migrations'";
            var count = await cmd.ExecuteScalarAsync(default).ConfigureAwait(false);
            System.Convert.ToInt32(count, System.Globalization.CultureInfo.InvariantCulture).Should().Be(1);
        }
    }

    [Fact]
    public async Task CreateHistoryTable_is_idempotent()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);
        var dialect = new SqliteMigrationDialect();

        await fixture.ExecuteDdlAsync(dialect.CreateHistoryTableSql).ConfigureAwait(false);
        // Second run must not throw — CREATE TABLE IF NOT EXISTS is the contract.
        await fixture.ExecuteDdlAsync(dialect.CreateHistoryTableSql).ConfigureAwait(false);
    }

    [Fact]
    public async Task SelectAppliedVersions_on_empty_table_returns_empty()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);
        var dialect = new SqliteMigrationDialect();
        await fixture.ExecuteDdlAsync(dialect.CreateHistoryTableSql).ConfigureAwait(false);

        var versions = await ReadVersionsAsync(fixture.Connection, dialect).ConfigureAwait(false);

        versions.Should().BeEmpty();
    }

    [Fact]
    public async Task InsertAppliedVersion_then_SelectAppliedVersions_returns_inserted_version()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);
        var dialect = new SqliteMigrationDialect();
        await fixture.ExecuteDdlAsync(dialect.CreateHistoryTableSql).ConfigureAwait(false);

        await InsertRowAsync(fixture.Connection, dialect, version: 7, name: "lucky_seven").ConfigureAwait(false);
        await InsertRowAsync(fixture.Connection, dialect, version: 8, name: "next_one").ConfigureAwait(false);

        var versions = await ReadVersionsAsync(fixture.Connection, dialect).ConfigureAwait(false);

        versions.Should().Equal(7, 8);
    }

    private static async Task InsertRowAsync(IAsyncDbConnection conn, SqliteMigrationDialect dialect, int version, string name)
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
            pAt.Value = System.DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            cmd.Parameters.Add(pAt);

            await cmd.ExecuteNonQueryAsync(default).ConfigureAwait(false);
        }
    }

    private static async Task<System.Collections.Generic.List<int>> ReadVersionsAsync(IAsyncDbConnection conn, SqliteMigrationDialect dialect)
    {
        var list = new System.Collections.Generic.List<int>();
        var cmd = conn.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = dialect.SelectAppliedVersionsSql;
            var reader = await cmd.ExecuteReaderAsync(default).ConfigureAwait(false);
            await using (((System.IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
            {
                while (await reader.ReadAsync(default).ConfigureAwait(false))
                {
                    list.Add(reader.GetInt32(0));
                }
            }
        }
        return list;
    }
}
