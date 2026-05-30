using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

public class EnumIntRoundTripTests
{
    // Default enum binding: the enum is sent down as its underlying integer
    // and read back via reader.GetInt32 + (Status) cast.
    [Fact]
    public async Task Reads_seeded_row_with_int_backed_enum()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Items (Id INTEGER PRIMARY KEY, Status INTEGER NOT NULL);
                INSERT INTO Items (Id, Status) VALUES (1, 1);").ConfigureAwait(false);

            var repo = new EnumIntRepo(fx.Connection);
            var row = await repo.GetByStatusAsync(Status.Cancelled, CancellationToken.None).ConfigureAwait(false);

            row.Should().NotBeNull();
            row!.Id.Should().Be(1);
            row.Status.Should().Be(Status.Cancelled);
        }
    }
}
