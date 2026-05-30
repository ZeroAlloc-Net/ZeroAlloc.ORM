using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

public class FlatRowReadTests
{
    [Fact]
    public async Task Reads_seeded_row_into_positional_record()
    {
        var fx = new SqliteFixture();
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
        var fx = new SqliteFixture();
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
