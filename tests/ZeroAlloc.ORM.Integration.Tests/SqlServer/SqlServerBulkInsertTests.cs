using System.Globalization;
using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.SqlServer;

// #263 — SQL Server round-trip coverage for [Command(Kind = BulkInsert)]. Full
// parity with the Sqlite (BulkInsertTests) and Postgres (PostgresBulkInsertTests)
// six-cell suites; there was no runtime BulkInsert coverage for SQL Server before
// this fix (docs/cookbook/bulk-insert.md's per-provider table marked it
// "snapshot-only" — see the report for how that gap was closed here).
//
// Two of the six cells (rows-affected, VO column) use the SAME plain
// `INSERT ... VALUES` SQL as Sqlite/Postgres — provider-agnostic T-SQL — so
// they reuse the shared BulkInsertRepo directly, exactly as
// PostgresBulkInsertTests does. The other four need SQL Server's
// `OUTPUT INSERTED.<col>` clause (which sits before VALUES, not trailing like
// `RETURNING`), so they go through SqlServerBulkInsertRepo instead:
//
//   * Insert_5_rows_returns_rows_affected            — rows-affected path (shared repo).
//   * Insert_5_rows_with_OUTPUT_returns_identity_list — OUTPUT INSERTED.Id path.
//   * Insert_1000_rows_forces_chunking                — 3 chunks at 450/chunk (mirrors
//     Sqlite's row count — SQL Server's real 2100-parameter cap is much closer to the
//     900-parameter budget than Postgres's 65535, so there's no equivalent case for
//     pushing to Postgres's 5000-row/12-chunk drill).
//   * Empty_collection_returns_zero                   — empty short-circuit (shared repo).
//   * Insert_row_with_value_object_column             — TRow with VO column (shared repo).
//   * Insert_with_OUTPUT_NULL_throws_naming_the_column_and_method — #263 NULL guard.
public sealed class SqlServerBulkInsertTests : IAsyncLifetime
{
    private static readonly int[] ExpectedCustomers5 = { 10, 20, 30, 40, 50 };
    private static readonly decimal[] ExpectedTotals5 = { 1.00m, 2.00m, 3.00m, 4.00m, 5.00m };
    private static readonly int[] ExpectedIds5 = { 1, 2, 3, 4, 5 };
    private static readonly int[] ExpectedCustomersVo = { 100, 200, 300 };
    private static readonly decimal[] ExpectedTotalsVo = { 11.00m, 22.00m, 33.00m };

    private readonly SqlServerFixture _fx = new();

    public ValueTask InitializeAsync() => _fx.InitializeAsync();
    public ValueTask DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Insert_5_rows_returns_rows_affected()
    {
        await SeedSchemaAsync().ConfigureAwait(false);

        var repo = new BulkInsertRepo(_fx.Connection);
        var rows = new[]
        {
            new BulkOrderRow(10, 1.00m),
            new BulkOrderRow(20, 2.00m),
            new BulkOrderRow(30, 3.00m),
            new BulkOrderRow(40, 4.00m),
            new BulkOrderRow(50, 5.00m),
        };

        var affected = await repo.InsertOrdersAsync(rows, CancellationToken.None).ConfigureAwait(false);

        affected.Should().Be(5);

        var seen = await QueryAllOrdersAsync().ConfigureAwait(false);
        seen.Should().HaveCount(5);
        seen.Select(r => r.CustomerId).Should().BeEquivalentTo(ExpectedCustomers5);
        seen.Select(r => r.Total).Should().BeEquivalentTo(ExpectedTotals5);
    }

    [Fact]
    public async Task Insert_5_rows_with_OUTPUT_returns_identity_list()
    {
        await SeedSchemaAsync().ConfigureAwait(false);

        var repo = new SqlServerBulkInsertRepo(_fx.Connection);
        var rows = new[]
        {
            new BulkOrderRow(10, 1.00m),
            new BulkOrderRow(20, 2.00m),
            new BulkOrderRow(30, 3.00m),
            new BulkOrderRow(40, 4.00m),
            new BulkOrderRow(50, 5.00m),
        };

        var ids = await repo.InsertOrdersReturningIdsAsync(rows, CancellationToken.None).ConfigureAwait(false);

        ids.Should().HaveCount(5);
        // IDENTITY(1,1) allocates 1..N for the first batch inserted into an
        // empty table. OUTPUT INSERTED.Id surfaces each new id in VALUES order.
        ids.Should().BeEquivalentTo(ExpectedIds5, opts => opts.WithStrictOrdering());

        var seen = await QueryAllOrdersAsync().ConfigureAwait(false);
        seen.Should().HaveCount(5);
        seen.Select(r => r.CustomerId).Should().BeEquivalentTo(ExpectedCustomers5);
        seen.Select(r => r.Total).Should().BeEquivalentTo(ExpectedTotals5);
    }

    [Fact]
    public async Task Insert_1000_rows_forces_chunking()
    {
        // 1000 rows / 450 chunk size = 3 chunks (450, 450, 100). The chunk-loop
        // emit must (a) execute all three iterations, (b) clear + re-prime the
        // SQL StringBuilder per chunk, and (c) accumulate the OUTPUT ids into a
        // single result list spanning the chunks — same shape as the Sqlite
        // drill (BulkInsertTests.Insert_1000_rows_forces_chunking).
        await SeedSchemaAsync().ConfigureAwait(false);

        var repo = new SqlServerBulkInsertRepo(_fx.Connection);
        var rows = new BulkOrderRow[1000];
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = new BulkOrderRow(CustomerId: i + 1, Total: (i + 1) * 0.01m);
        }

        var ids = await repo.InsertOrdersReturningIdsAsync(rows, CancellationToken.None).ConfigureAwait(false);

        ids.Should().HaveCount(1000);
        // Each id must be unique — the chunk loop must not re-bind from a
        // previous chunk's OUTPUT stream.
        ids.Distinct().Should().HaveCount(1000);

        var cmd = _fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Orders";
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false), CultureInfo.InvariantCulture);
            count.Should().Be(1000);
        }
    }

    [Fact]
    public async Task Empty_collection_returns_zero()
    {
        await SeedSchemaAsync().ConfigureAwait(false);

        var repo = new BulkInsertRepo(_fx.Connection);

        // Snapshot the table state BEFORE the no-op call so we can prove the
        // empty short-circuit doesn't smuggle any rows into the table.
        var probeBefore = _fx.Connection.CreateCommand();
        int countBefore;
        await using (probeBefore.ConfigureAwait(false))
        {
            probeBefore.CommandText = "SELECT COUNT(*) FROM Orders";
            countBefore = Convert.ToInt32(await probeBefore.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }

        var affected = await repo.InsertOrdersAsync(Array.Empty<BulkOrderRow>(), CancellationToken.None).ConfigureAwait(false);

        affected.Should().Be(0);

        var probeAfter = _fx.Connection.CreateCommand();
        await using (probeAfter.ConfigureAwait(false))
        {
            probeAfter.CommandText = "SELECT COUNT(*) FROM Orders";
            var countAfter = Convert.ToInt32(await probeAfter.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false), CultureInfo.InvariantCulture);
            countAfter.Should().Be(countBefore);
        }
    }

    [Fact]
    public async Task Insert_row_with_value_object_column()
    {
        // TRow's CustomerId column is the existing [ValueObject] wrapper
        // `CustomerId` (struct with int Value + From factory). The generator's
        // per-row parameter binding unwraps `row.CustomerId.Value` on the way
        // down to the DbParameter — the SingleArgCtor/ValueObject convention
        // path that EmitBulkInsertCommand's BuildBulkInsertParameterValueExpression
        // routes through. Re-running it against SqlClient confirms the unwrap
        // survives the DBNull.Value guard wrapper Task 6 introduced.
        await SeedSchemaAsync().ConfigureAwait(false);

        var repo = new BulkInsertRepo(_fx.Connection);
        var rows = new[]
        {
            new BulkOrderRowWithVo(CustomerId.From(100), 11.00m),
            new BulkOrderRowWithVo(CustomerId.From(200), 22.00m),
            new BulkOrderRowWithVo(CustomerId.From(300), 33.00m),
        };

        var affected = await repo.InsertOrdersWithVoAsync(rows, CancellationToken.None).ConfigureAwait(false);

        affected.Should().Be(3);

        var seen = await QueryAllOrdersAsync().ConfigureAwait(false);
        seen.Should().HaveCount(3);
        seen.Select(r => r.CustomerId).Should().BeEquivalentTo(ExpectedCustomersVo);
        seen.Select(r => r.Total).Should().BeEquivalentTo(ExpectedTotalsVo);
    }

    // #263 — the identity readback had no NULL guard. OUTPUT NULL forces a NULL
    // identity for every row; SqlClient throws SqlNullValueException for a NULL
    // read through GetInt32 — the third of the three provider exception types
    // the guard's filter checks, and the only one not already covered by the
    // Sqlite / Postgres reproductions.
    [Fact]
    public async Task Insert_with_OUTPUT_NULL_throws_naming_the_column_and_method()
    {
        await SeedSchemaAsync().ConfigureAwait(false);

        var repo = new SqlServerBulkInsertRepo(_fx.Connection);
        var rows = new[] { new BulkOrderRow(10, 1.00m) };

        var ex = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.InsertOrdersReturningNullIdAsync(rows, CancellationToken.None)).ConfigureAwait(false);

        ex.InnerException.Should().BeOfType<System.Data.SqlTypes.SqlNullValueException>();
        ex.Message.Should().Contain("is NULL");
        ex.Message.Should().Contain(
            "ZeroAlloc.ORM.Integration.Tests.SqlServer.SqlServerBulkInsertRepo.InsertOrdersReturningNullIdAsync");
        ex.Message.Should().Contain("non-nullable 'int'");

        // The row was still inserted — the exception fires while draining the
        // OUTPUT reader, after SQL Server already committed the INSERT.
        var seen = await QueryAllOrdersAsync().ConfigureAwait(false);
        seen.Should().HaveCount(1);
    }

    private async Task SeedSchemaAsync() => await ExecuteAsync("""
        CREATE TABLE Orders (
            Id INT IDENTITY(1,1) PRIMARY KEY,
            CustomerId INT NOT NULL,
            Total DECIMAL(18,2) NOT NULL)
        """).ConfigureAwait(false);

    private async Task ExecuteAsync(string sql)
    {
        var cmd = _fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<List<(int Id, int CustomerId, decimal Total)>> QueryAllOrdersAsync()
    {
        var result = new List<(int Id, int CustomerId, decimal Total)>();
        var cmd = _fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT Id, CustomerId, Total FROM Orders ORDER BY Id";
            var reader = await cmd.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
            await using (((IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
            {
                while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    result.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2)));
                }
            }
        }
        return result;
    }
}
