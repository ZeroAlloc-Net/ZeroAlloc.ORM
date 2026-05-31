using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// v0.3 Phase E.1 — round-trip integration coverage for MultiResultSet emit.
// Three scenarios:
//   * HeadAndLines_Auto_returns_tuple — (OrderRow, IReadOnlyList<OrderLineRow>) via
//     BatchMode.Auto. Sqlite's Microsoft.Data.Sqlite >= 9.x exposes CanCreateBatch
//     so the runtime branch picks the IAsyncDbBatch path on this provider.
//   * HeadAndLines_Never_returns_tuple_via_joined_sql — same shape forced through
//     the ;-joined single-command fallback via BatchMode.Never. Proves both emit
//     paths produce the same materialization on the same data.
//   * ThreeElementTuple_Auto_returns_count_first_all — 3-element tuple of
//     (Scalar Count, Row First, List<Row> All) demonstrating the scalar +
//     row + list mix lands across three ;-separated SELECTs.
public class MultiResultSetTests
{
    [Fact]
    public async Task HeadAndLines_Auto_returns_tuple()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);
                CREATE TABLE OrderLines (Id INTEGER PRIMARY KEY, OrderId INTEGER NOT NULL, Sku TEXT NOT NULL, Qty INTEGER NOT NULL);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (42, 100, 99.95);
                INSERT INTO OrderLines (Id, OrderId, Sku, Qty) VALUES (1, 42, 'sku-a', 2);
                INSERT INTO OrderLines (Id, OrderId, Sku, Qty) VALUES (2, 42, 'sku-b', 1);").ConfigureAwait(false);

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
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);
                CREATE TABLE OrderLines (Id INTEGER PRIMARY KEY, OrderId INTEGER NOT NULL, Sku TEXT NOT NULL, Qty INTEGER NOT NULL);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (42, 100, 99.95);
                INSERT INTO OrderLines (Id, OrderId, Sku, Qty) VALUES (1, 42, 'sku-a', 2);
                INSERT INTO OrderLines (Id, OrderId, Sku, Qty) VALUES (2, 42, 'sku-b', 1);").ConfigureAwait(false);

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
    public async Task ThreeElementTuple_Auto_returns_count_first_all()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 42, 10.00);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (2, 42, 20.00);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (3, 99, 30.00);").ConfigureAwait(false);

            var repo = new MultiResultSetRepo(fx.Connection);
            var result = await repo.GetCountFirstAllAsync(CancellationToken.None).ConfigureAwait(false);

            result.Should().NotBeNull();
            result!.Value.Count.Should().Be(3);
            result.Value.First.Should().Be(new OrderRow(1, 42, 10.00m));
            result.Value.All.Should().HaveCount(3);
            result.Value.All[0].Should().Be(new OrderRow(1, 42, 10.00m));
            result.Value.All[1].Should().Be(new OrderRow(2, 42, 20.00m));
            result.Value.All[2].Should().Be(new OrderRow(3, 99, 30.00m));
        }
    }
}
