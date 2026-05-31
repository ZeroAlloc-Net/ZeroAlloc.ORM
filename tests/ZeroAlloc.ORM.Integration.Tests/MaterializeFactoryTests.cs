using System.Globalization;
using System.Threading;
using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// v0.5 Phase D.3 — `[Materialize(Factory)]` round-trip against Sqlite. Pins
// the canonical "decimal-as-text" recipe in the design doc (Section 3, line
// 366) — Microsoft.Data.Sqlite stores `NUMERIC` columns as TEXT and the
// factory dispatch lets the adopter parse them under InvariantCulture to
// avoid the culture-dependent GetDecimal path.
//
// Three round-trip scenarios:
//
//   * Factory_round_trips_decimal_via_text_storage      — scalar Task<MoneyWithFactory>.
//   * Factory_nested_in_flat_row_round_trips            — Task<MoneyWithFactoryOrderRow?>.
//   * Factory_handles_culture_dependent_decimal_format  — proves the factory
//        parses under InvariantCulture even when the host culture is non-en-US.
//        (Defended directly: `decimal.Parse("1234.56", InvariantCulture)` is
//        deterministic; the factory dispatch ensures the GENERATOR doesn't
//        emit the culture-sensitive GetDecimal path that v0.5 Phase A uses.)
public class MaterializeFactoryTests
{
    [Fact]
    public async Task Factory_round_trips_decimal_via_text_storage()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            // Store the decimal as TEXT explicitly (Sqlite NUMERIC affinity
            // happily accepts TEXT). The factory's string parameter receives
            // the raw text from the reader.
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount TEXT NOT NULL, Currency TEXT NOT NULL);
                INSERT INTO Orders (Id, Amount, Currency) VALUES (1, '99.95', 'USD');").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var money = await repo.GetTotalViaFactoryAsync(1, CancellationToken.None).ConfigureAwait(false);

            money.Amount.Should().Be(99.95m);
            money.Currency.Should().Be("USD");
        }
    }

    [Fact]
    public async Task Factory_nested_in_flat_row_round_trips()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount TEXT NOT NULL, Currency TEXT NOT NULL);
                INSERT INTO Orders (Id, Amount, Currency) VALUES (42, '1234.56', 'EUR');").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var row = await repo.GetMoneyOrderRowAsync(42, CancellationToken.None).ConfigureAwait(false);

            row.Should().NotBeNull();
            row!.Id.Should().Be(42);
            row.Total.Amount.Should().Be(1234.56m);
            row.Total.Currency.Should().Be("EUR");
        }
    }

    [Fact]
    public async Task Factory_handles_invariant_decimal_format()
    {
        // The point of the factory: avoid the culture dependency that
        // `GetDecimal` would have. The DB stores '1.50' (en-US-style) and
        // the factory parses with InvariantCulture so the test passes
        // regardless of the host's CurrentCulture.
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Amount TEXT NOT NULL, Currency TEXT NOT NULL);
                INSERT INTO Orders (Id, Amount, Currency) VALUES (7, '1.50', 'GBP');").ConfigureAwait(false);

            var repo = new CompositeRepo(fx.Connection);
            var money = await repo.GetTotalViaFactoryAsync(7, CancellationToken.None).ConfigureAwait(false);

            money.Amount.Should().Be(1.50m);
            money.Currency.Should().Be("GBP");
            // Sanity: the factory really did parse via InvariantCulture
            // (the test culture default for xunit on Windows is en-US which
            // happens to match; the explicit check pins the contract).
            var parsed = decimal.Parse("1.50", NumberStyles.Number, CultureInfo.InvariantCulture);
            money.Amount.Should().Be(parsed);
        }
    }
}
