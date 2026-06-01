using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// v0.6 Phase A.4 — `[Materialize(Factory)]` round-trip against Postgres.
//
// Investigation note (locked behaviour, NOT a bug):
//
// We initially tried running the factory against a NUMERIC(18,4) column
// on the assumption that Npgsql would convert the column to its textual
// form on `GetString` (matching the factory's `string amountText`
// parameter). It does not — Npgsql raises:
//
//   InvalidCastException: Reading as 'System.String' is not supported
//   for fields having DataTypeName 'numeric'
//
// This means the factory pattern is **storage-type-coupled**: the column
// type must match the factory parameter type. When an adopter chooses the
// `[Materialize(Factory = "FromStorage")]` recipe with `string amountText`,
// they're committing to storing the column as TEXT (Sqlite or Postgres)
// rather than NUMERIC. On Postgres NUMERIC the right path is the plain
// composite ctor (no factory) — covered by `CompositeTests` in this folder.
//
// So this test runs the factory dispatch against a TEXT column on
// Postgres, proving:
//
//   1. The factory dispatch IS provider-agnostic at the ZA.ORM layer
//      (the generator emits the same `MoneyWithFactory.FromStorage(
//      reader.GetString(ord), ...)` shape regardless of provider).
//   2. The Sqlite Phase D recipe ports cleanly to Postgres when the
//      adopter mirrors the storage convention.
//
// The Sqlite mirror remains the canonical "decimal-as-text" Phase D
// recipe; this Postgres mirror is the symmetric proof on a different
// substrate.
[Trait("Provider", "Postgres")]
public sealed class MaterializeFactoryTests
{
    [Fact]
    public async Task Factory_round_trips_via_text_storage()
    {
        var fx = new PostgresFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            // TEXT column matches the factory's `string amountText` parameter.
            // This is the storage shape an adopter using the factory recipe
            // commits to — `[Materialize(Factory = "FromStorage")]` with a
            // string parameter implies a TEXT column at the wire level.
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
    public async Task Factory_nested_in_flat_row_round_trips_via_text_storage()
    {
        var fx = new PostgresFixture();
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
}
