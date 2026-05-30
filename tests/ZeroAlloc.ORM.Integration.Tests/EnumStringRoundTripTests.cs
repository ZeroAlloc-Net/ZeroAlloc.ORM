using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

public class EnumStringRoundTripTests
{
    // [StoreAsString] binding: the parameter goes down as "Cancelled" and
    // reads come back via reader.GetString + Enum.Parse<StringStatus>.
    [Fact]
    public async Task Reads_seeded_row_with_string_backed_enum()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Items (Id INTEGER PRIMARY KEY, Status TEXT NOT NULL);
                INSERT INTO Items (Id, Status) VALUES (1, 'Cancelled');").ConfigureAwait(false);

            var repo = new EnumStringRepo(fx.Connection);
            var row = await repo.GetByStatusAsync(StringStatus.Cancelled, CancellationToken.None).ConfigureAwait(false);

            row.Should().NotBeNull();
            row!.Id.Should().Be(1);
            row.Status.Should().Be(StringStatus.Cancelled);
        }
    }
}
