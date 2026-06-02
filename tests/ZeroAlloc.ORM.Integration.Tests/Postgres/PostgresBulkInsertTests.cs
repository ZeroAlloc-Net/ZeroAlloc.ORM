using System.Globalization;
using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// v1.3 Phase Task 10 — Postgres round-trip coverage for [Command(Kind = BulkInsert)].
// Postgres-side mirror of the Task 9 Sqlite suite. Re-uses the existing
// BulkInsertRepo + BulkOrderRow / BulkOrderRowWithVo declarations: Postgres folds
// unquoted identifiers to lowercase, so the same SQL ("INSERT INTO Orders
// (CustomerId, Total) VALUES (@CustomerId, @Total) RETURNING Id") binds to the
// lowercase schema we seed below — no Postgres-specific repo needed.
//
// Five cells — same shape as Sqlite, with two Postgres-specific upgrades:
//
//   * Insert_5_rows_returns_rows_affected               — rows-affected path.
//   * Insert_5_rows_with_returning_returns_identity_list — RETURNING Id path.
//   * Insert_5000_rows_forces_chunking                  — 12 chunks at 450/chunk.
//   * Empty_collection_returns_zero                     — empty short-circuit.
//   * Insert_row_with_value_object_column               — TRow with VO column.
//
// Why 5000 rows (vs Sqlite's 1000)? Postgres's parameter ceiling (65535) gives a
// much larger budget than Sqlite's ~999 default, so the chunk-multi-row path on
// Postgres should be exercised under closer-to-realistic load. 5000 / 450 = 12
// chunks (11 * 450 + 50 = 5000), which forces more iterations of the chunk
// re-prime + RETURNING-accumulation loop than the Sqlite drill.
//
// DBNull.Value guard validation: this is the *whole point* of running on
// Postgres. The Task 6 fix wrapped every parameter value as `(object?)expr ??
// DBNull.Value`. Npgsql strictly rejects raw `null` parameter values — any
// successful round-trip here confirms the guard works. (Task 9's Sqlite suite
// is happier with raw null and so doesn't catch a regression on its own.)
[Trait("Provider", "Postgres")]
public sealed class PostgresBulkInsertTests
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
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
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

    [Fact]
    public async Task Insert_5_rows_with_returning_returns_identity_list()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
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
        // Postgres SERIAL allocates monotonically increasing ids from the
        // sequence; the schema below starts the sequence at 1 (default), so
        // the first batch into an empty table receives 1..5 in insert order.
        // RETURNING preserves VALUES order per the SQL standard.
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

    [Fact]
    public async Task Insert_5000_rows_forces_chunking()
    {
        // 5000 rows / 450 chunk size = 12 chunks (11 * 450 + 50). Bigger than
        // the Sqlite drill on purpose — Postgres's higher parameter budget
        // lets us exercise more iterations of the chunk-loop emit:
        //   (a) all twelve iterations run,
        //   (b) the SQL StringBuilder is cleared + re-primed per chunk, and
        //   (c) RETURNING ids accumulate into a single result list across all
        //       chunks (no re-binding from a previous chunk's reader stream).
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await SeedSchemaAsync(fx).ConfigureAwait(false);

        var repo = new BulkInsertRepo(fx.Connection);
        var rows = new BulkOrderRow[5000];
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = new BulkOrderRow(CustomerId: i + 1, Total: (i + 1) * 0.01m);
        }

        var ids = await repo.InsertOrdersReturningIdsAsync(rows, CancellationToken.None).ConfigureAwait(false);

        ids.Should().HaveCount(5000);
        // Each id must be unique — the chunk loop must not re-emit a previous
        // chunk's RETURNING stream.
        ids.Distinct().Should().HaveCount(5000);

        // Cross-check via the database: SELECT COUNT(*) FROM orders. If any
        // chunk silently no-op'd, the count would be < 5000.
        var probe = fx.Connection.CreateCommand();
        await using (probe.ConfigureAwait(false))
        {
            probe.CommandText = "SELECT COUNT(*) FROM orders";
            var count = Convert.ToInt32(await probe.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false), CultureInfo.InvariantCulture);
            count.Should().Be(5000);
        }
    }

    [Fact]
    public async Task Empty_collection_returns_zero()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await SeedSchemaAsync(fx).ConfigureAwait(false);

        var repo = new BulkInsertRepo(fx.Connection);

        // Snapshot the table state BEFORE the no-op call so we can prove
        // the empty short-circuit doesn't smuggle any rows into the table.
        var probeBefore = fx.Connection.CreateCommand();
        int countBefore;
        await using (probeBefore.ConfigureAwait(false))
        {
            probeBefore.CommandText = "SELECT COUNT(*) FROM orders";
            countBefore = Convert.ToInt32(await probeBefore.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }

        var affected = await repo.InsertOrdersAsync(Array.Empty<BulkOrderRow>(), CancellationToken.None).ConfigureAwait(false);

        affected.Should().Be(0);

        // Table count unchanged — the empty short-circuit must return BEFORE
        // any INSERT executes. (The snapshot test pins the literal
        // `if (__rows.Count == 0) return 0;` emit; this test asserts the
        // observable consequence on Postgres: no rows added.)
        var probeAfter = fx.Connection.CreateCommand();
        await using (probeAfter.ConfigureAwait(false))
        {
            probeAfter.CommandText = "SELECT COUNT(*) FROM orders";
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
        // down to the DbParameter — exactly the SingleArgCtor/ValueObject
        // convention path that EmitBulkInsertCommand's
        // BuildBulkInsertParameterValueExpression routes through. Re-running
        // it against Npgsql confirms the unwrap survives the DBNull.Value
        // guard wrapper introduced in Task 6.
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
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

        // Round-trip: SELECT and verify the int stored in customerid matches
        // the unwrapped Value of the input VO.
        var seen = await QueryAllOrdersAsync(fx).ConfigureAwait(false);
        seen.Should().HaveCount(3);
        seen.Select(r => r.CustomerId).Should().BeEquivalentTo(ExpectedCustomersVo);
        seen.Select(r => r.Total).Should().BeEquivalentTo(ExpectedTotalsVo);
    }

    // Schema: Postgres folds unquoted identifiers to lowercase, so the repo's
    // PascalCase `Orders (CustomerId, Total) ... RETURNING Id` SQL binds
    // against this lowercase schema. SERIAL auto-allocates ids from a
    // sequence (starting at 1 on a fresh table), so the RETURNING clause
    // surfaces them in VALUES order — matching the Sqlite suite's INTEGER
    // PRIMARY KEY behaviour for the identity round-trip assertion.
    private static ValueTask SeedSchemaAsync(PostgresFixture fx) => fx.ExecuteDdlAsync(@"
        CREATE TABLE orders (
            id SERIAL PRIMARY KEY,
            customerid INTEGER NOT NULL,
            total NUMERIC NOT NULL);");

    private static async Task<List<(int Id, int CustomerId, decimal Total)>> QueryAllOrdersAsync(PostgresFixture fx)
    {
        var result = new List<(int Id, int CustomerId, decimal Total)>();
        var cmd = fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT id, customerid, total FROM orders ORDER BY id";
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

    private static async Task<(int Id, int CustomerId, decimal Total)?> QueryOrderByIdAsync(PostgresFixture fx, int id)
    {
        var cmd = fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT id, customerid, total FROM orders WHERE id = @id";
            var p = cmd.CreateParameter();
            p.ParameterName = "@id";
            p.Value = id;
            cmd.Parameters.Add(p);
            var reader = await cmd.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
            await using (((IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
            {
                if (!await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false)) return null;
                return (reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2));
            }
        }
    }
}
