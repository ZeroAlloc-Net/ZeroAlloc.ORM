using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// v0.6 Phase A.3 — first real-server integration coverage for the v0.4
// [StoredProcedure] pipeline + the Postgres-idiomatic function-via-[Query]
// path. Sqlite has no native sprocs, so the v0.4 placeholder
// `tests/ZeroAlloc.ORM.Integration.Tests/StoredProcedureTests.cs` skipped
// with a deferral note pointing here.
//
// Postgres has two stored-logic mechanisms:
//
//   * FUNCTIONs — invoked via `SELECT * FROM fn(@args)`. Cookbook recommends
//     this route for rowset returns; ZA.ORM routes it through `[Query]`,
//     NOT `[StoredProcedure]`. The procedure-vs-function note in
//     `docs/cookbook/stored-procedures.md` (Provider quirks) records this.
//
//   * PROCEDUREs — invoked via `CALL proc(@args)`. ZA.ORM routes it through
//     `[StoredProcedure]` with `CommandType.StoredProcedure`. Output values
//     come through INOUT (PG 14) or OUT (PG 15+) parameters.
//
// Test matrix:
//
//   * Function_via_Query_returns_single_row — function returning a single
//     row via [Query], proving the rowset-via-FUNCTION pattern.
//   * Procedure_with_output_parameter_round_trips — real CREATE PROCEDURE
//     with INOUT param, called via [StoredProcedure] + named-tuple. Proves
//     the v0.4 output-param emit lights up on Postgres.
//   * MultiResultSet_via_function_calls_returns_count_and_rows — multi-
//     result-set tuple via `;`-joined SELECTs against functions, BatchMode.Auto
//     so the IAsyncDbBatch branch fires (CanCreateBatch == true on Npgsql).
//
// Class name prefixed with `Postgres` so `~StoredProcedureTests` matches
// only the Sqlite placeholder, not this real-server suite (review caught
// the collision).
[Trait("Provider", "Postgres")]
public sealed class PostgresStoredProcedureTests
{
    [Fact]
    public async Task Function_via_Query_returns_single_row()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE TABLE orders (id INTEGER PRIMARY KEY, customerid INTEGER NOT NULL, total NUMERIC NOT NULL);
            INSERT INTO orders (id, customerid, total) VALUES (42, 100, 99.95);
            CREATE FUNCTION get_order_fn(p_id integer)
                RETURNS TABLE(id integer, customerid integer, total numeric)
                LANGUAGE sql
            AS $$
                SELECT id, customerid, total FROM orders WHERE id = p_id;
            $$;").ConfigureAwait(false);

        var repo = new StoredProcedureRepo(fx.Connection);
        var row = await repo.GetOrderViaFunctionAsync(42, CancellationToken.None).ConfigureAwait(false);

        row.Should().NotBeNull();
        row!.Id.Should().Be(42);
        row.CustomerId.Should().Be(100);
        row.Total.Should().Be(99.95m);
    }

    [Fact]
    public async Task Procedure_with_output_parameter_round_trips()
    {
        // OUT-only parameters require PG 15+ (the fixture pins
        // postgres:16-alpine, so this is safe). INOUT would also work
        // wire-side, but the ZA.ORM generator emits
        // `Direction = Output` (NOT InputOutput) for named-tuple
        // output slots — INOUT on the Postgres side combined with
        // Direction=Output on the C# side means the seed value never
        // reaches the procedure body, which would surface as a DBNull
        // on the output read. OUT on the procedure matches the
        // generator's Direction=Output convention cleanly.
        //
        // The procedure assigns fixed values to the OUT slots so the
        // assertions don't depend on the seed values the caller
        // passes through the C# method.
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE PROCEDURE allocate_id_proc(OUT neworderid integer, OUT status integer)
                LANGUAGE plpgsql
            AS $$
            BEGIN
                neworderid := 1042;
                status := 7;
            END;
            $$;").ConfigureAwait(false);

        var repo = new StoredProcedureRepo(fx.Connection);
        var (newId, status) = await repo.AllocateIdAsync(0, 0, CancellationToken.None).ConfigureAwait(false);

        newId.Should().Be(1042, "the procedure assigns a constant id to the OUT slot");
        status.Should().Be(7, "the procedure assigns a constant status to the OUT slot");
    }

    [Fact]
    public async Task MultiResultSet_via_function_calls_returns_count_and_rows()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE TABLE orders (id INTEGER PRIMARY KEY, customerid INTEGER NOT NULL, total NUMERIC NOT NULL);
            INSERT INTO orders (id, customerid, total) VALUES (1, 42, 10.00);
            INSERT INTO orders (id, customerid, total) VALUES (2, 42, 20.00);
            INSERT INTO orders (id, customerid, total) VALUES (3, 99, 30.00);").ConfigureAwait(false);

        // Pins the substrate assumption — same logic as PostgresMultiResultSetTests.
        fx.Connection.CanCreateBatch.Should().BeTrue();

        var repo = new StoredProcedureRepo(fx.Connection);
        var result = await repo.GetOrdersAndCountAsync(CancellationToken.None).ConfigureAwait(false);

        result.Should().NotBeNull();
        result!.Value.Count.Should().Be(3);
        result.Value.All.Should().HaveCount(3);
        result.Value.All[0].Should().Be(new OrderRow(1, 42, 10.00m));
        result.Value.All[1].Should().Be(new OrderRow(2, 42, 20.00m));
        result.Value.All[2].Should().Be(new OrderRow(3, 99, 30.00m));
    }
}
