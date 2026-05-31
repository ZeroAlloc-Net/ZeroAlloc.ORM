using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// v0.5 Phase B.3 — composite parameter BINDING round-trip against Sqlite.
// Mirror of CompositeTests (which exercises the materialization side) — each
// test seeds a small Orders table, exercises one composite binding shape,
// and asserts the data wrote/read back correctly.
//
//   * Insert_with_composite_parameter            -- INSERT … VALUES (@id,
//                                                   @total_Amount, @total_Currency)
//                                                   with `Money total`.
//   * Update_with_composite_where_clause         -- composite parameter on
//                                                   both sides of a WHERE
//                                                   predicate.
//   * Round_trip_insert_then_select              -- write via composite
//                                                   binding (B.2), read back
//                                                   via composite
//                                                   materialization (A.4).
//   * Insert_with_composite_containing_value_object_inner
//                                                -- composite whose inner
//                                                   field is a VO. Pins the
//                                                   layered `.Value` unwrap
//                                                   at bind time.
//
// Sqlite stores decimals as TEXT; the integer-valued decimals used here
// round-trip via Microsoft.Data.Sqlite without loss (same caveat as
// CompositeTests). FluentAssertions `.Should().Be(decimal)` trips EPS06
// (hidden struct copy), so decimal assertions use Xunit.Assert.Equal
// matching the CommandNonQueryTests pattern.
public class CompositeBindingIntegrationTests
{
    [Fact]
    public async Task Insert_with_composite_parameter()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NOT NULL, Currency TEXT NOT NULL);").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var total = new Money(199.99m, "USD");
            var rowsAffected = await repo.InsertOrderAsync(1, total, CancellationToken.None).ConfigureAwait(false);

            rowsAffected.Should().Be(1);

            // Read back via raw command to verify the values landed in the
            // right columns — this is the binding-side test so we don't yet
            // assert through the materialization repo (that's the round-trip
            // test below).
            var probe = fx.Connection.CreateCommand();
            await using (probe.ConfigureAwait(false))
            {
                probe.CommandText = "SELECT Amount, Currency FROM Orders WHERE Id = 1";
                var reader = await probe.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
                await using (((System.IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
                {
                    var advanced = await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false);
                    advanced.Should().BeTrue();
                    Assert.Equal(199.99m, reader.GetDecimal(0));
                    reader.GetString(1).Should().Be("USD");
                }
            }
        }
    }

    [Fact]
    public async Task Update_with_composite_where_clause()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NOT NULL, Currency TEXT NOT NULL);
                INSERT INTO Orders (Id, Amount, Currency) VALUES (10, 50.00, 'EUR');
                INSERT INTO Orders (Id, Amount, Currency) VALUES (11, 50.00, 'USD');").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var predicate = new Money(50.00m, "EUR");
            var rowsAffected = await repo.UpdateAmountAsync(predicate, 75.00m, CancellationToken.None).ConfigureAwait(false);

            // Only row 10 matches the (Amount, Currency) composite predicate.
            // Row 11 has the same Amount but a different Currency and must
            // remain untouched — pins both fields being threaded through.
            rowsAffected.Should().Be(1);

            var probe = fx.Connection.CreateCommand();
            await using (probe.ConfigureAwait(false))
            {
                probe.CommandText = "SELECT Amount FROM Orders WHERE Id = 10";
                var updated = await probe.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.Equal(75.00m, Convert.ToDecimal(updated, System.Globalization.CultureInfo.InvariantCulture));
            }
            var probe2 = fx.Connection.CreateCommand();
            await using (probe2.ConfigureAwait(false))
            {
                probe2.CommandText = "SELECT Amount FROM Orders WHERE Id = 11";
                var untouched = await probe2.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.Equal(50.00m, Convert.ToDecimal(untouched, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }

    [Fact]
    public async Task Round_trip_insert_then_select()
    {
        // Pin the full B + A loop: write via composite parameter binding
        // (B.2 emit), read back via composite materialization (A.4 emit).
        // A regression in either side surfaces here.
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NOT NULL, Currency TEXT NOT NULL);").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var total = new Money(1234.56m, "GBP");
            await repo.InsertOrderAsync(42, total, CancellationToken.None).ConfigureAwait(false);

            var roundTripped = await repo.GetTotalAsync(42, CancellationToken.None).ConfigureAwait(false);

            Assert.Equal(total, roundTripped);
            Assert.Equal(1234.56m, roundTripped.Amount);
            roundTripped.Currency.Should().Be("GBP");
        }
    }

    [Fact]
    public async Task Insert_with_composite_containing_value_object_inner()
    {
        // The inner Currency field of MoneyWithOrderId is an OrderId (VO).
        // The emitted bind expression is `@total.Currency.Value` — the test
        // pins the `.Value` unwrap by SELECTing the raw INTEGER column and
        // comparing against the underlying primitive 7, NOT against
        // `OrderId.From(7)`. A regression that forgets the VO unwrap would
        // store either DBNull or a struct-formatted string and trip a
        // GetInt32 InvalidCastException here.
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount NUMERIC NOT NULL, Currency INTEGER NOT NULL);").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var total = new MoneyWithOrderId(50.00m, OrderId.From(7));
            var rowsAffected = await repo.InsertMoneyWithOrderIdAsync(1, total, CancellationToken.None).ConfigureAwait(false);

            rowsAffected.Should().Be(1);

            var probe = fx.Connection.CreateCommand();
            await using (probe.ConfigureAwait(false))
            {
                probe.CommandText = "SELECT Amount, Currency FROM Orders WHERE Id = 1";
                var reader = await probe.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
                await using (((System.IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
                {
                    var advanced = await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false);
                    advanced.Should().BeTrue();
                    Assert.Equal(50.00m, reader.GetDecimal(0));
                    reader.GetInt32(1).Should().Be(7);
                }
            }
        }
    }
}
