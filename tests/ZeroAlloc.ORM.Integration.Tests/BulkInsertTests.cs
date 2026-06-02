using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// v1.3 Phase Task 9 — Sqlite round-trip coverage for [Command(Kind = BulkInsert)].
// Five cells validate runtime behaviour of the chunked-INSERT pipeline emitted
// by EmitBulkInsertCommand. The generator's snapshot tests already pin the
// EMIT SHAPE; these tests prove the SHAPE works against a real provider.
//
//   * Insert_5_rows_returns_rows_affected               — rows-affected path.
//   * Insert_5_rows_with_returning_returns_identity_list — RETURNING Id path.
//   * Insert_1000_rows_forces_chunking                  — 1000 / 450 = 3 chunks.
//   * Empty_collection_returns_zero                     — empty short-circuit.
//   * Insert_row_with_value_object_column               — TRow with VO property.
//
// 450 = 900 / 2 placeholders per row (CustomerId, Total), folded as a constant
// by the generator.
public class BulkInsertTests
{
    // CA1861 — hoist constant array arguments to static readonly fields.
    private static readonly int[] ExpectedCustomers5 = { 10, 20, 30, 40, 50 };
    private static readonly decimal[] ExpectedTotals5 = { 1.00m, 2.00m, 3.00m, 4.00m, 5.00m };
    private static readonly int[] ExpectedIds5 = { 1, 2, 3, 4, 5 };
    private static readonly int[] ExpectedCustomersVo = { 100, 200, 300 };
    private static readonly decimal[] ExpectedTotalsVo = { 11.00m, 22.00m, 33.00m };

    [Fact]
    public async Task Insert_5_rows_returns_rows_affected()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);

            var repo = new BulkInsertRepo(fx.Connection);
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

            // Confirm every row is queryable with the inserted values.
            var seen = await QueryAllOrdersAsync(fx).ConfigureAwait(false);
            seen.Should().HaveCount(5);
            seen.Select(r => r.CustomerId).Should().BeEquivalentTo(ExpectedCustomers5);
            seen.Select(r => r.Total).Should().BeEquivalentTo(ExpectedTotals5);
        }
    }

    [Fact]
    public async Task Insert_5_rows_with_returning_returns_identity_list()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);

            var repo = new BulkInsertRepo(fx.Connection);
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
            // Sqlite's INTEGER PRIMARY KEY allocates 1..N for the first batch
            // inserted into an empty table. The RETURNING clause must surface
            // each new rowid in insert-order.
            ids.Should().BeEquivalentTo(ExpectedIds5, opts => opts.WithStrictOrdering());

            // Round-trip: SELECT the returned ids and confirm each row's data
            // matches the corresponding input element.
            for (var i = 0; i < ids.Count; i++)
            {
                var fetched = await QueryOrderByIdAsync(fx, ids[i]).ConfigureAwait(false);
                fetched.Should().NotBeNull();
                fetched!.Value.CustomerId.Should().Be(rows[i].CustomerId);
                fetched.Value.Total.Should().Be(rows[i].Total);
            }
        }
    }

    [Fact]
    public async Task Insert_1000_rows_forces_chunking()
    {
        // 1000 rows / 450 chunk size = 3 chunks (450, 450, 100). The chunk-loop
        // emit must (a) execute all three iterations, (b) clear + re-prime the
        // SQL StringBuilder per chunk, and (c) accumulate the RETURNING ids
        // into a single result list spanning the chunks.
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);

            var repo = new BulkInsertRepo(fx.Connection);
            var rows = new BulkOrderRow[1000];
            for (var i = 0; i < rows.Length; i++)
            {
                rows[i] = new BulkOrderRow(CustomerId: i + 1, Total: (i + 1) * 0.01m);
            }

            var ids = await repo.InsertOrdersReturningIdsAsync(rows, CancellationToken.None).ConfigureAwait(false);

            ids.Should().HaveCount(1000);
            // Each id must be unique — the chunk loop must not re-bind from a
            // previous chunk's RETURNING stream.
            ids.Distinct().Should().HaveCount(1000);

            // Cross-check via the database: SELECT COUNT(*) FROM Orders. If any
            // chunk silently no-op'd, the count would be < 1000.
            var probe = fx.Connection.CreateCommand();
            await using (probe.ConfigureAwait(false))
            {
                probe.CommandText = "SELECT COUNT(*) FROM Orders";
                var count = Convert.ToInt32(await probe.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                count.Should().Be(1000);
            }
        }
    }

    [Fact]
    public async Task Empty_collection_returns_zero()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);

            var repo = new BulkInsertRepo(fx.Connection);

            // Snapshot the table state BEFORE the no-op call so we can prove
            // the empty short-circuit doesn't smuggle any rows into the table.
            var probeBefore = fx.Connection.CreateCommand();
            int countBefore;
            await using (probeBefore.ConfigureAwait(false))
            {
                probeBefore.CommandText = "SELECT COUNT(*) FROM Orders";
                countBefore = Convert.ToInt32(await probeBefore.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            }

            var affected = await repo.InsertOrdersAsync(Array.Empty<BulkOrderRow>(), CancellationToken.None).ConfigureAwait(false);

            affected.Should().Be(0);

            // Table count unchanged — the empty short-circuit must return
            // BEFORE any INSERT executes. (The snapshot test pins the literal
            // `if (__rows.Count == 0) return 0;` emit; this test asserts the
            // observable consequence: no rows added.)
            var probeAfter = fx.Connection.CreateCommand();
            await using (probeAfter.ConfigureAwait(false))
            {
                probeAfter.CommandText = "SELECT COUNT(*) FROM Orders";
                var countAfter = Convert.ToInt32(await probeAfter.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                countAfter.Should().Be(countBefore);
            }
        }
    }

    [Fact]
    public async Task Insert_row_with_value_object_column()
    {
        // TRow's CustomerId column is the existing [ValueObject] wrapper
        // `CustomerId` (struct with int Value + From factory). The generator's
        // per-row parameter binding unwraps `row.CustomerId.Value` on the way
        // down to the DbParameter — exactly the SingleArgCtor/ValueObject
        // convention path that EmitBulkInsertCommand's
        // BuildBulkInsertParameterValueExpression routes through.
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);

            var repo = new BulkInsertRepo(fx.Connection);
            var rows = new[]
            {
                new BulkOrderRowWithVo(CustomerId.From(100), 11.00m),
                new BulkOrderRowWithVo(CustomerId.From(200), 22.00m),
                new BulkOrderRowWithVo(CustomerId.From(300), 33.00m),
            };

            var affected = await repo.InsertOrdersWithVoAsync(rows, CancellationToken.None).ConfigureAwait(false);

            affected.Should().Be(3);

            // Round-trip: SELECT and verify the int stored in CustomerId
            // matches the unwrapped Value of the input VO.
            var seen = await QueryAllOrdersAsync(fx).ConfigureAwait(false);
            seen.Should().HaveCount(3);
            seen.Select(r => r.CustomerId).Should().BeEquivalentTo(ExpectedCustomersVo);
            seen.Select(r => r.Total).Should().BeEquivalentTo(ExpectedTotalsVo);
        }
    }

    // Schema: Sqlite's `INTEGER PRIMARY KEY` is the ROWID alias, so values
    // omitted from the INSERT auto-generate starting at 1. The BulkInsert SQL
    // never names Id in the column list — the auto-increment path is exactly
    // what makes RETURNING Id meaningful.
    private static ValueTask SeedSchemaAsync(SqliteFixture fx) => fx.ExecuteDdlAsync(@"
        CREATE TABLE Orders (
            Id INTEGER PRIMARY KEY,
            CustomerId INTEGER NOT NULL,
            Total NUMERIC NOT NULL);");

    private static async Task<List<(int Id, int CustomerId, decimal Total)>> QueryAllOrdersAsync(SqliteFixture fx)
    {
        var result = new List<(int Id, int CustomerId, decimal Total)>();
        var cmd = fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT Id, CustomerId, Total FROM Orders ORDER BY Id";
            var reader = await cmd.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
            await using (((System.IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
            {
                while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    result.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2)));
                }
            }
        }
        return result;
    }

    private static async Task<(int Id, int CustomerId, decimal Total)?> QueryOrderByIdAsync(SqliteFixture fx, int id)
    {
        var cmd = fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id";
            var p = cmd.CreateParameter();
            p.ParameterName = "@id";
            p.Value = id;
            cmd.Parameters.Add(p);
            var reader = await cmd.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
            await using (((System.IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
            {
                if (!await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false)) return null;
                return (reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2));
            }
        }
    }
}
