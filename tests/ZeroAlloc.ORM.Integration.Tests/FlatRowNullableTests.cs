using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

public class FlatRowNullableTests
{
    [Fact]
    public async Task Nullable_columns_round_trip_correctly()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Flex (Id INTEGER PRIMARY KEY, OptionalCount INTEGER, OptionalName TEXT);
                INSERT INTO Flex (Id, OptionalCount, OptionalName) VALUES (1, NULL, NULL);").ConfigureAwait(false);

            var repo = new FlexRowRepo(fx.Connection);
            var row = await repo.GetFirstAsync(CancellationToken.None).ConfigureAwait(false);

            row.Should().NotBeNull();
            row!.Id.Should().Be(1);
            row.OptionalCount.Should().BeNull();
            row.OptionalName.Should().BeNull();
        }
    }

    [Fact]
    public async Task Nullable_columns_round_trip_non_null_values()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Flex (Id INTEGER PRIMARY KEY, OptionalCount INTEGER, OptionalName TEXT);
                INSERT INTO Flex (Id, OptionalCount, OptionalName) VALUES (1, 7, 'present');").ConfigureAwait(false);

            var repo = new FlexRowRepo(fx.Connection);
            var row = await repo.GetFirstAsync(CancellationToken.None).ConfigureAwait(false);

            row.Should().NotBeNull();
            row!.Id.Should().Be(1);
            row.OptionalCount.Should().Be(7);
            row.OptionalName.Should().Be("present");
        }
    }
}
