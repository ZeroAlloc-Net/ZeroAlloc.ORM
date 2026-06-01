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
[Trait("Provider", "Postgres")]
public sealed class FlatRowReadTests
{
    [Fact]
    public async Task Reads_seeded_row_into_positional_record()
    {
        var fx = new PostgresFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 42, 99.95);").ConfigureAwait(false);

            var repo = new FlatRowRepo(fx.Connection);
            var row = await repo.GetFirstAsync(CancellationToken.None).ConfigureAwait(false);

            row.Should().NotBeNull();
            row!.Id.Should().Be(1);
            row.CustomerId.Should().Be(42);
            row.Total.Should().Be(99.95m);
        }
    }

    [Fact]
    public async Task Empty_table_returns_null()
    {
        var fx = new PostgresFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);").ConfigureAwait(false);

            var repo = new FlatRowRepo(fx.Connection);
            var row = await repo.GetFirstAsync(CancellationToken.None).ConfigureAwait(false);

            row.Should().BeNull();
        }
    }
}
