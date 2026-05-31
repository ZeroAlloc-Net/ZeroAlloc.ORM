using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// v0.5 Phase A.4 — composite materialization round-trips against Sqlite.
//
// Each test seeds a small Orders table whose schema mirrors the SELECT list
// the repo's [Query] expects, then exercises one composite shape:
//
//   * Scalar_composite_round_trips_money — Task<Money>.
//   * Nested_composite_in_flat_row_round_trips — Task<CompositeOrderRow?>.
//   * Scalar_composite_with_value_object_field_round_trips —
//       Task<MoneyWithOrderId>; the composite's second ctor parameter is a VO.
//
// Sqlite stores decimals as TEXT; the integer-valued decimals used here
// round-trip via Microsoft.Data.Sqlite's GetDecimal without loss.
public class CompositeTests
{
    [Fact]
    public async Task Scalar_composite_round_trips_money()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NOT NULL, Currency TEXT NOT NULL);
                INSERT INTO Orders (Id, Amount, Currency) VALUES (1, 99.95, 'USD');").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var money = await repo.GetTotalAsync(1, CancellationToken.None).ConfigureAwait(false);

            money.Amount.Should().Be(99.95m);
            money.Currency.Should().Be("USD");
        }
    }

    [Fact]
    public async Task Nested_composite_in_flat_row_round_trips()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NOT NULL, Currency TEXT NOT NULL);
                INSERT INTO Orders (Id, Amount, Currency) VALUES (42, 1234.56, 'EUR');").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var row = await repo.GetCompositeOrderRowAsync(42, CancellationToken.None).ConfigureAwait(false);

            row.Should().NotBeNull();
            row!.Id.Should().Be(42);
            row.Total.Amount.Should().Be(1234.56m);
            row.Total.Currency.Should().Be("EUR");
        }
    }

    [Fact]
    public async Task Nested_composite_in_flat_row_returns_null_for_missing_row()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NOT NULL, Currency TEXT NOT NULL);").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var row = await repo.GetCompositeOrderRowAsync(999, CancellationToken.None).ConfigureAwait(false);

            row.Should().BeNull();
        }
    }

    [Fact]
    public async Task Nested_composite_in_domain_entity_round_trips()
    {
        // v0.5 Phase A (post-review Fix 2) — DomainEntity round-trip mirror of
        // `Nested_composite_in_flat_row_round_trips`. The repo SELECTs columns in
        // reverse order (Currency, Amount, Id) so a regression in the
        // EmitNestedCompositeConstructionByOrdinalName ordinal-by-name path would
        // surface as a runtime InvalidCastException rather than passing on luck.
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NOT NULL, Currency TEXT NOT NULL);
                INSERT INTO Orders (Id, Amount, Currency) VALUES (7, 250.00, 'GBP');").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var entity = await repo.GetOrderEntityAsync(7, CancellationToken.None).ConfigureAwait(false);

            entity.Should().NotBeNull();
            entity!.Id.Should().Be(7);
            entity.Total.Amount.Should().Be(250.00m);
            entity.Total.Currency.Should().Be("GBP");
        }
    }

    [Fact]
    public async Task Scalar_composite_on_empty_table_throws_ZeroAllocOrmMaterializationException()
    {
        // v0.5 Phase A (post-review Fix 3) — EmitComposite throws on an empty
        // result-set because the composite return is non-nullable (Task<Money>,
        // not Task<Money?>). Nullable composites are Phase C's all-or-nothing
        // branch; for now the throw is the contract and this test pins it.
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NOT NULL, Currency TEXT NOT NULL);").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            Func<Task> act = () => repo.GetTotalAsync(1, CancellationToken.None);

            await act.Should().ThrowAsync<ZeroAllocOrmMaterializationException>()
                .WithMessage("*Composite scalar query returned no row*").ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Scalar_composite_with_value_object_field_round_trips()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NOT NULL, Currency INTEGER NOT NULL);
                INSERT INTO Orders (Id, Amount, Currency) VALUES (1, 50.00, 7);").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var money = await repo.GetMoneyWithOrderIdAsync(1, CancellationToken.None).ConfigureAwait(false);

            money.Amount.Should().Be(50.00m);
            money.Currency.Value.Should().Be(7);
        }
    }
}
