using FluentAssertions;
using Xunit;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// v0.6 Phase A.4 — composite materialization round-trips against Postgres.
// Mirrors `CompositeTests` (scalar / nested-in-flat-row / nested-in-domain-
// entity / VO-inner-field) plus a subset of `NullableCompositeTests` (all-
// or-nothing branch with null + populated + mixed-null cases) against a
// real-decimal provider.
//
// Why re-run on Postgres? The Sqlite suite exercises GetDecimal under
// Microsoft.Data.Sqlite's "NUMERIC stored as TEXT" idiom — the round-trip
// happens to work because integer-valued decimals parse cleanly, but the
// underlying read goes through string conversion. Postgres returns native
// `decimal` from `NUMERIC(p,s)`, so this re-run proves the composite ctor
// dispatch lights up on a provider that doesn't lean on the text-conversion
// path at all.
//
// Class name prefixed with `Postgres` so the test filter
// `FullyQualifiedName~CompositeTests` matches only the Sqlite suite, and
// `~PostgresCompositeTests` matches only this one.
[Trait("Provider", "Postgres")]
public sealed class PostgresCompositeTests
{
    [Fact]
    public async Task Scalar_composite_round_trips_money()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE TABLE orders (id INTEGER PRIMARY KEY, amount NUMERIC(18,4) NOT NULL, currency TEXT NOT NULL);
            INSERT INTO orders (id, amount, currency) VALUES (1, 99.95, 'USD');").ConfigureAwait(false);

        var repo = new CompositeRepo(fx.Connection);
        var money = await repo.GetTotalAsync(1, CancellationToken.None).ConfigureAwait(false);

        money.Amount.Should().Be(99.95m);
        money.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Nested_composite_in_flat_row_round_trips()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE TABLE orders (id INTEGER PRIMARY KEY, amount NUMERIC(18,4) NOT NULL, currency TEXT NOT NULL);
            INSERT INTO orders (id, amount, currency) VALUES (42, 1234.56, 'EUR');").ConfigureAwait(false);

        var repo = new CompositeRepo(fx.Connection);
        var row = await repo.GetCompositeOrderRowAsync(42, CancellationToken.None).ConfigureAwait(false);

        row.Should().NotBeNull();
        row!.Id.Should().Be(42);
        row.Total.Amount.Should().Be(1234.56m);
        row.Total.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task Nested_composite_in_domain_entity_round_trips()
    {
        // SELECT column order is reverse (Currency, Amount, Id) to prove the
        // DomainEntity GetOrdinal(name) lookup carries — same regression pin
        // as the Sqlite mirror.
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE TABLE orders (id INTEGER PRIMARY KEY, amount NUMERIC(18,4) NOT NULL, currency TEXT NOT NULL);
            INSERT INTO orders (id, amount, currency) VALUES (7, 250.00, 'GBP');").ConfigureAwait(false);

        var repo = new CompositeRepo(fx.Connection);
        var entity = await repo.GetOrderEntityAsync(7, CancellationToken.None).ConfigureAwait(false);

        entity.Should().NotBeNull();
        entity!.Id.Should().Be(7);
        entity.Total.Amount.Should().Be(250.00m);
        entity.Total.Currency.Should().Be("GBP");
    }

    [Fact]
    public async Task Nullable_composite_all_null_returns_null()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE TABLE orders (id INTEGER PRIMARY KEY, amount NUMERIC(18,4) NULL, currency TEXT NULL);
            INSERT INTO orders (id, amount, currency) VALUES (1, NULL, NULL);").ConfigureAwait(false);

        var repo = new CompositeRepo(fx.Connection);
        var money = await repo.GetNullableTotalAsync(1, CancellationToken.None).ConfigureAwait(false);

        money.Should().BeNull();
    }

    [Fact]
    public async Task Nullable_composite_both_populated_returns_money()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE TABLE orders (id INTEGER PRIMARY KEY, amount NUMERIC(18,4) NULL, currency TEXT NULL);
            INSERT INTO orders (id, amount, currency) VALUES (2, 42.50, 'USD');").ConfigureAwait(false);

        var repo = new CompositeRepo(fx.Connection);
        var money = await repo.GetNullableTotalAsync(2, CancellationToken.None).ConfigureAwait(false);

        money.Should().NotBeNull();
        money!.Value.Amount.Should().Be(42.50m);
        money.Value.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Nullable_composite_mixed_null_throws_with_diagnostic_message()
    {
        // The all-or-nothing throw must fire BEFORE any driver-level type
        // error — Postgres NUMERIC(18,4) NULL + TEXT 'EUR' is a legal row
        // server-side, so a regression that lets the materialization run
        // would surface as a Money(0m, "EUR") rather than an exception.
        // The throw is the contract.
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE TABLE orders (id INTEGER PRIMARY KEY, amount NUMERIC(18,4) NULL, currency TEXT NULL);
            INSERT INTO orders (id, amount, currency) VALUES (3, NULL, 'EUR');").ConfigureAwait(false);

        var repo = new CompositeRepo(fx.Connection);
        var act = async () => await repo.GetNullableTotalAsync(3, CancellationToken.None).ConfigureAwait(false);

        await act.Should().ThrowAsync<ZeroAllocOrmMaterializationException>()
            .Where(e => e.Message.Contains("mixed-null", StringComparison.Ordinal))
            .ConfigureAwait(false);
    }

    [Fact]
    public async Task Scalar_composite_with_value_object_field_round_trips()
    {
        // MoneyWithOrderId's second ctor parameter is an OrderId VO over int —
        // INTEGER on the Postgres side matches the VO's int-underlying read.
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE TABLE orders (id INTEGER PRIMARY KEY, amount NUMERIC(18,4) NOT NULL, currency INTEGER NOT NULL);
            INSERT INTO orders (id, amount, currency) VALUES (1, 50.00, 7);").ConfigureAwait(false);

        var repo = new CompositeRepo(fx.Connection);
        var money = await repo.GetMoneyWithOrderIdAsync(1, CancellationToken.None).ConfigureAwait(false);

        money.Amount.Should().Be(50.00m);
        money.Currency.Value.Should().Be(7);
    }
}
