using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// v0.6 Phase A.2 — Postgres-backed mirror of FlatRowReadTests. The flat-row
// materialization path is fully exercised on Sqlite; this re-runs it on a
// real-decimal provider so the NUMERIC → System.Decimal binding gets
// runtime coverage on the substrate that v0.5 Phase D Money.FromStorage
// indirectly depends on (Sqlite stores decimal as text and converts on
// read; Postgres returns native decimal, which the column-direct read
// path handles without the text-conversion dance).
//
// Class name prefixed with `Postgres` so `dotnet test --filter
// FullyQualifiedName~FlatRowReadTests` only matches the Sqlite suite and
// `~PostgresFlatRowReadTests` only matches this one — the v0.6 Phase A
// review caught the collision between the two same-named classes.
[Trait("Provider", "Postgres")]
public sealed class PostgresFlatRowReadTests
{
    [Fact]
    public async Task Reads_seeded_row_into_positional_record()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE TABLE orders (id INTEGER PRIMARY KEY, customerid INTEGER NOT NULL, total NUMERIC NOT NULL);
            INSERT INTO orders (id, customerid, total) VALUES (1, 42, 99.95);").ConfigureAwait(false);

        var repo = new FlatRowRepo(fx.Connection);
        var row = await repo.GetFirstAsync(CancellationToken.None).ConfigureAwait(false);

        row.Should().NotBeNull();
        row!.Id.Should().Be(1);
        row.CustomerId.Should().Be(42);
        row.Total.Should().Be(99.95m);
    }

    [Fact]
    public async Task Empty_table_returns_null()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE TABLE orders (id INTEGER PRIMARY KEY, customerid INTEGER NOT NULL, total NUMERIC NOT NULL);").ConfigureAwait(false);

        var repo = new FlatRowRepo(fx.Connection);
        var row = await repo.GetFirstAsync(CancellationToken.None).ConfigureAwait(false);

        row.Should().BeNull();
    }
}
