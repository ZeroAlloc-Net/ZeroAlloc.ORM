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
    public async Task Procedure_with_output_parameters_of_each_type_round_trips()
    {
        // v2.0, #235 — the same shape as SqlServerStoredProcedureTests. Every
        // output now carries a DbType and the text output Size = -1. Npgsql
        // writes NULL for each OUT argument and fills the parameters from the
        // row the CALL returns, so the values are unaffected.
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE PROCEDURE output_types_proc(
                IN seed integer,
                OUT count integer,
                OUT label text,
                OUT total numeric(18,4),
                OUT rounded numeric,
                OUT traceid uuid,
                OUT at timestamp)
                LANGUAGE plpgsql
            AS $$
            BEGIN
                count := seed * 2;
                label := repeat('x', 5000);
                total := 1234.5678;
                rounded := 1234.5678;
                traceid := '6f9619ff-8b86-d011-b42d-00c04fc964ff';
                at := '2024-01-02 03:04:05.123456';
            END;
            $$;").ConfigureAwait(false);

        var repo = new StoredProcedureRepo(fx.Connection);
        var result = await repo.OutputTypesAsync(
            seed: 21, count: 0, label: "", total: 0m, rounded: 0m,
            traceid: Guid.Empty, at: default, CancellationToken.None).ConfigureAwait(false);

        result.Count.Should().Be(42);
        result.Label.Should().Be(new string('x', 5000));
        result.Total.Should().Be(1234.5678m);
        result.Rounded.Should().Be(1234.5678m, "Npgsql returns a numeric OUT value exactly without a Scale");
        result.Traceid.Should().Be(new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff"));
        result.At.Should().Be(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Unspecified).AddTicks(1_234_560));
    }

    [Fact]
    public async Task Procedure_with_inout_parameters_sends_and_reads_back()
    {
        // v2.0, #235 — [Param(Direction = InputOutput)] sends the argument, so an
        // INOUT parameter sees it; with the default Output direction Npgsql would
        // write NULL for it.
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE PROCEDURE input_output_proc(INOUT counter integer, INOUT label text)
                LANGUAGE plpgsql
            AS $$
            BEGIN
                counter := counter + 1;
                label := label || '!';
            END;
            $$;").ConfigureAwait(false);

        var repo = new StoredProcedureRepo(fx.Connection);
        var (counter, label) = await repo.InputOutputAsync(41, "hi", CancellationToken.None).ConfigureAwait(false);

        counter.Should().Be(42);
        label.Should().Be("hi!");
    }

    // #244 — a NULL OUT value into a non-nullable tuple element throws
    // ZeroAllocOrmMaterializationException naming the procedure and the
    // parameter. Before, a string became "" and a value type threw a bare
    // InvalidCastException from Convert.
    [Fact]
    public async Task Null_output_into_non_nullable_string_throws_naming_the_parameter()
    {
        await using var fx = await CreateFixtureWithNullOutputProcAsync().ConfigureAwait(false);
        var repo = new StoredProcedureRepo(fx.Connection);

        await AssertNullOutputThrowsAsync(
            () => repo.NullIntoStringAsync("seed", null, null, null, CancellationToken.None), "note").ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_output_into_non_nullable_int_throws_naming_the_parameter()
    {
        await using var fx = await CreateFixtureWithNullOutputProcAsync().ConfigureAwait(false);
        var repo = new StoredProcedureRepo(fx.Connection);

        await AssertNullOutputThrowsAsync(
            () => repo.NullIntoIntAsync(null, 0, null, null, CancellationToken.None), "amount").ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_output_into_non_nullable_enum_throws_naming_the_parameter()
    {
        await using var fx = await CreateFixtureWithNullOutputProcAsync().ConfigureAwait(false);
        var repo = new StoredProcedureRepo(fx.Connection);

        await AssertNullOutputThrowsAsync(
            () => repo.NullIntoEnumAsync(null, null, Status.Pending, null, CancellationToken.None), "state").ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_output_into_non_nullable_value_object_throws_naming_the_parameter()
    {
        await using var fx = await CreateFixtureWithNullOutputProcAsync().ConfigureAwait(false);
        var repo = new StoredProcedureRepo(fx.Connection);

        await AssertNullOutputThrowsAsync(
            () => repo.NullIntoValueObjectAsync(null, null, null, new OrderId(0), CancellationToken.None), "orderref").ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_output_into_nullable_targets_reads_back_as_null()
    {
        await using var fx = await CreateFixtureWithNullOutputProcAsync().ConfigureAwait(false);
        var repo = new StoredProcedureRepo(fx.Connection);

        var result = await repo.NullIntoNullableAsync("seed", 1, Status.Cancelled, new OrderId(1), CancellationToken.None).ConfigureAwait(false);

        result.Note.Should().BeNull();
        result.Amount.Should().BeNull();
        result.State.Should().BeNull();
        result.Orderref.Should().BeNull();
    }

    private static async Task AssertNullOutputThrowsAsync(Func<Task> call, string parameterName)
    {
        var thrown = await call.Should().ThrowAsync<ZeroAllocOrmMaterializationException>().ConfigureAwait(false);
        thrown.Which.Message.Should().Contain("'null_output_proc'").And.Contain($"'{parameterName}'");
    }

    private static async Task<PostgresFixture> CreateFixtureWithNullOutputProcAsync()
    {
        var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        try
        {
            await CreateNullOutputProcAsync(fx).ConfigureAwait(false);
            return fx;
        }
        catch
        {
            await fx.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static ValueTask CreateNullOutputProcAsync(PostgresFixture fx) => fx.ExecuteDdlAsync(@"
            CREATE PROCEDURE null_output_proc(
                OUT note text,
                OUT amount integer,
                OUT state integer,
                OUT orderref integer)
                LANGUAGE plpgsql
            AS $$
            BEGIN
                note := NULL;
                amount := NULL;
                state := NULL;
                orderref := NULL;
            END;
            $$;");

    [Fact]
    public async Task Return_value_parameter_throws_because_Postgres_has_no_RETURN_value()
    {
        // #241 — a Postgres procedure has no RETURN value. The generator cannot
        // see the provider, so it emits the ReturnValue parameter it emits for
        // SQL Server. Npgsql leaves it out of the CALL, which succeeds, and never
        // sets it, so its Value stays null. Read as an int that would be 0; the
        // generated code throws instead.
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE PROCEDURE return_value_proc(IN seed integer, OUT doubled integer)
                LANGUAGE plpgsql
            AS $$
            BEGIN
                doubled := seed * 2;
            END;
            $$;").ConfigureAwait(false);

        var repo = new StoredProcedureRepo(fx.Connection);
        var act = async () => await repo.ReturnValueAsync(21, 0, null, CancellationToken.None).ConfigureAwait(false);

        (await act.Should().ThrowAsync<ZeroAllocOrmMaterializationException>().ConfigureAwait(false))
            .WithMessage("*did not set the RETURN value*'ReturnValueAsync'*'RETURN_VALUE'*Only SQL Server*");
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
