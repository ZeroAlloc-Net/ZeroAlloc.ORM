using FluentAssertions;
using Xunit;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// v0.5 Phase C.3 — nullable composite round-trips against Sqlite.
//
// Coverage:
//   * Scalar Task<Money?>:
//       - all-null DB row -> returns null.
//       - both columns populated -> returns Money(amount, currency).
//       - mixed-null -> throws ZeroAllocOrmMaterializationException with a
//         message naming the mixed column state.
//   * Nested nullable composite (NullableMoneyOrderRow(int Id, Money? Total)):
//       - Total populated -> NullableMoneyOrderRow with non-null Total.
//       - Total all-null  -> NullableMoneyOrderRow with null Total.
//       - mixed-null      -> throws.
//   * Nullable composite parameter (Option A):
//       - null parameter writes DBNull for both columns; the row's composite
//         is therefore all-null and the round-trip read returns null.
//       - non-null parameter writes the unpacked values; round-trip returns
//         the same Money.
//
// Sqlite stores `NUMERIC` as TEXT for decimals. Microsoft.Data.Sqlite's
// GetDecimal handles the conversion for the integer-valued decimals used here.
public class NullableCompositeTests
{
    [Fact]
    public async Task Nullable_composite_all_null_returns_null()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NULL, Currency TEXT NULL);
                INSERT INTO Orders (Id, Amount, Currency) VALUES (1, NULL, NULL);").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var money = await repo.GetNullableTotalAsync(1, CancellationToken.None).ConfigureAwait(false);

            money.Should().BeNull();
        }
    }

    [Fact]
    public async Task Nullable_composite_both_populated_returns_money()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NULL, Currency TEXT NULL);
                INSERT INTO Orders (Id, Amount, Currency) VALUES (2, 42.50, 'USD');").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var money = await repo.GetNullableTotalAsync(2, CancellationToken.None).ConfigureAwait(false);

            money.Should().NotBeNull();
            money!.Value.Amount.Should().Be(42.50m);
            money.Value.Currency.Should().Be("USD");
        }
    }

    [Fact]
    public async Task Nullable_composite_mixed_null_throws_with_diagnostic_message()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NULL, Currency TEXT NULL);
                INSERT INTO Orders (Id, Amount, Currency) VALUES (3, NULL, 'EUR');").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var act = async () => await repo.GetNullableTotalAsync(3, CancellationToken.None).ConfigureAwait(false);

            await act.Should().ThrowAsync<ZeroAllocOrmMaterializationException>()
                .Where(e => e.Message.Contains("mixed-null", StringComparison.Ordinal))
                .ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Nullable_composite_in_flat_row_round_trips_populated()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NULL, Currency TEXT NULL);
                INSERT INTO Orders (Id, Amount, Currency) VALUES (10, 12.34, 'GBP');").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var row = await repo.GetNullableMoneyRowAsync(10, CancellationToken.None).ConfigureAwait(false);

            row.Should().NotBeNull();
            row!.Id.Should().Be(10);
            row.Total.Should().NotBeNull();
            row.Total!.Value.Amount.Should().Be(12.34m);
            row.Total.Value.Currency.Should().Be("GBP");
        }
    }

    [Fact]
    public async Task Nullable_composite_in_flat_row_round_trips_all_null()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NULL, Currency TEXT NULL);
                INSERT INTO Orders (Id, Amount, Currency) VALUES (11, NULL, NULL);").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var row = await repo.GetNullableMoneyRowAsync(11, CancellationToken.None).ConfigureAwait(false);

            row.Should().NotBeNull();
            row!.Id.Should().Be(11);
            row.Total.Should().BeNull();
        }
    }

    [Fact]
    public async Task Nullable_composite_in_flat_row_mixed_null_throws()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NULL, Currency TEXT NULL);
                INSERT INTO Orders (Id, Amount, Currency) VALUES (12, 99.95, NULL);").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var act = async () => await repo.GetNullableMoneyRowAsync(12, CancellationToken.None).ConfigureAwait(false);

            await act.Should().ThrowAsync<ZeroAllocOrmMaterializationException>()
                .Where(e => e.Message.Contains("mixed-null", StringComparison.Ordinal))
                .ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Nullable_composite_parameter_null_writes_DBNull()
    {
        // Option A bind round-trip — `Money? total = null` writes DBNull for
        // both columns; the subsequent read sees the all-null row and the
        // nullable composite materializer returns null.
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NULL, Currency TEXT NULL);").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var inserted = await repo.InsertNullableTotalAsync(20, null, CancellationToken.None).ConfigureAwait(false);
            inserted.Should().Be(1);

            var read = await repo.GetNullableTotalAsync(20, CancellationToken.None).ConfigureAwait(false);
            read.Should().BeNull();
        }
    }

    [Fact]
    public async Task Nullable_composite_parameter_non_null_round_trips()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NULL, Currency TEXT NULL);").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var inserted = await repo.InsertNullableTotalAsync(21, new Money(7.25m, "AUD"), CancellationToken.None).ConfigureAwait(false);
            inserted.Should().Be(1);

            var read = await repo.GetNullableTotalAsync(21, CancellationToken.None).ConfigureAwait(false);
            read.Should().NotBeNull();
            read!.Value.Amount.Should().Be(7.25m);
            read.Value.Currency.Should().Be("AUD");
        }
    }
}
