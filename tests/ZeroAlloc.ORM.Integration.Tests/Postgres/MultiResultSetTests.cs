using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// v0.6 Phase A.2 — Postgres-backed mirror of the Sqlite MultiResultSetTests.
//
// On Sqlite the AdoNet.Async wrapper reports CanCreateBatch = false, so the
// Auto/Never tests collapse to the same ;-joined fallback. Npgsql properly
// implements DbBatch and AdoNet.Async forwards it, so the *Auto variants on
// this fixture exercise the IAsyncDbBatch runtime branch for the first time
// outside snapshot tests. The CanCreateBatch == true assertion at the top of
// each Auto test pins the substrate assumption: if it ever flips back to
// false (driver downgrade, AdoNet.Async forwarding regression), the test
// fails loudly with a localized assertion instead of silently re-routing
// through the fallback.
//
// Resolves backlog: v0.3-CLN3 — Postgres batch branch finally has runtime
// coverage. (Sqlite still pins the fallback branch via the on-main test.)
[Trait("Provider", "Postgres")]
public sealed class MultiResultSetTests
{
    [Fact]
    public async Task HeadAndLines_Auto_returns_tuple_via_batch_branch()
    {
        var fx = new PostgresFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedHeadAndLinesAsync(fx).ConfigureAwait(false);

            // Substrate assumption — exercises the IAsyncDbBatch branch in the
            // generator emit. If this drops to false the test below proves
            // nothing new beyond the joined-SQL fallback.
            fx.Connection.CanCreateBatch.Should().BeTrue();

            var repo = new MultiResultSetRepo(fx.Connection);
            var result = await repo.GetOrderWithLinesAutoAsync(42, CancellationToken.None).ConfigureAwait(false);

            result.Should().NotBeNull();
            result!.Value.Head.Should().Be(new OrderRow(42, 100, 99.95m));
            result.Value.Lines.Should().HaveCount(2);
            result.Value.Lines[0].Should().Be(new OrderLineRow(1, 42, "sku-a", 2));
            result.Value.Lines[1].Should().Be(new OrderLineRow(2, 42, "sku-b", 1));
        }
    }

    [Fact]
    public async Task HeadAndLines_Never_returns_tuple_via_joined_sql()
    {
        var fx = new PostgresFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedHeadAndLinesAsync(fx).ConfigureAwait(false);

            var repo = new MultiResultSetRepo(fx.Connection);
            var result = await repo.GetOrderWithLinesNeverAsync(42, CancellationToken.None).ConfigureAwait(false);

            result.Should().NotBeNull();
            result!.Value.Head.Should().Be(new OrderRow(42, 100, 99.95m));
            result.Value.Lines.Should().HaveCount(2);
            result.Value.Lines[0].Should().Be(new OrderLineRow(1, 42, "sku-a", 2));
            result.Value.Lines[1].Should().Be(new OrderLineRow(2, 42, "sku-b", 1));
        }
    }

    [Fact]
    public async Task ThreeElementTuple_Auto_returns_count_first_all_via_batch_branch()
    {
        var fx = new PostgresFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedThreeOrdersAsync(fx).ConfigureAwait(false);

            fx.Connection.CanCreateBatch.Should().BeTrue();

            var repo = new MultiResultSetRepo(fx.Connection);
            var result = await repo.GetCountFirstAllAsync(CancellationToken.None).ConfigureAwait(false);

            result.Should().NotBeNull();
            AssertCountFirstAll(result!.Value);
        }
    }

    [Fact]
    public async Task ThreeElementTuple_Never_returns_count_first_all_via_joined_sql()
    {
        var fx = new PostgresFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedThreeOrdersAsync(fx).ConfigureAwait(false);

            var repo = new MultiResultSetRepo(fx.Connection);
            var result = await repo.GetCountFirstAllNeverAsync(CancellationToken.None).ConfigureAwait(false);

            result.Should().NotBeNull();
            AssertCountFirstAll(result!.Value);
        }
    }

    // Postgres unquoted identifiers fold to lowercase. The MultiResultSetRepo
    // SQL writes `Orders`/`OrderLines`/`Id`/`CustomerId`/`Total`/etc. — Postgres
    // sees them as `orders`/`orderlines`/`id`/`customerid`/`total`. The
    // emit-side column lookup uses `DbDataReader.GetOrdinal(<name>)` which is
    // case-insensitive on Npgsql, so the materialization round-trips cleanly.
    // INTEGER → System.Int32, NUMERIC → System.Decimal, TEXT → System.String.
    private static ValueTask SeedHeadAndLinesAsync(PostgresFixture fx) => fx.ExecuteDdlAsync(@"
        CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);
        CREATE TABLE OrderLines (Id INTEGER PRIMARY KEY, OrderId INTEGER NOT NULL, Sku TEXT NOT NULL, Qty INTEGER NOT NULL);
        INSERT INTO Orders (Id, CustomerId, Total) VALUES (42, 100, 99.95);
        INSERT INTO OrderLines (Id, OrderId, Sku, Qty) VALUES (1, 42, 'sku-a', 2);
        INSERT INTO OrderLines (Id, OrderId, Sku, Qty) VALUES (2, 42, 'sku-b', 1);");

    private static ValueTask SeedThreeOrdersAsync(PostgresFixture fx) => fx.ExecuteDdlAsync(@"
        CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);
        INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 42, 10.00);
        INSERT INTO Orders (Id, CustomerId, Total) VALUES (2, 42, 20.00);
        INSERT INTO Orders (Id, CustomerId, Total) VALUES (3, 99, 30.00);");

    private static void AssertCountFirstAll((int Count, OrderRow First, IReadOnlyList<OrderRow> All) result)
    {
        result.Count.Should().Be(3);
        result.First.Should().Be(new OrderRow(1, 42, 10.00m));
        result.All.Should().HaveCount(3);
        result.All[0].Should().Be(new OrderRow(1, 42, 10.00m));
        result.All[1].Should().Be(new OrderRow(2, 42, 20.00m));
        result.All[2].Should().Be(new OrderRow(3, 99, 30.00m));
    }
}
