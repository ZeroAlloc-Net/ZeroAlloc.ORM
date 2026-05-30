using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

public class ValueObjectRoundTripTests
{
    // Round-trip: a CustomerId parameter is unwrapped to its Value (int) on the
    // way down to the Sqlite command, and the returned Id column is wrapped back
    // via CustomerId.From(int) into the materialized CustomerRow.
    [Fact]
    public async Task Reads_seeded_row_with_value_object_id()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);
                INSERT INTO Customers (Id, Name) VALUES (1, 'Alice');").ConfigureAwait(false);

            var repo = new ValueObjectRepo(fx.Connection);
            var row = await repo.GetAsync(CustomerId.From(1), CancellationToken.None).ConfigureAwait(false);

            row.Should().NotBeNull();
            row!.Id.Value.Should().Be(1);
            row.Name.Should().Be("Alice");
        }
    }

    [Fact]
    public async Task Missing_row_returns_null()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);").ConfigureAwait(false);

            var repo = new ValueObjectRepo(fx.Connection);
            var row = await repo.GetAsync(CustomerId.From(999), CancellationToken.None).ConfigureAwait(false);

            row.Should().BeNull();
        }
    }
}
