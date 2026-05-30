using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

public class ScalarReadTests
{
    [Fact]
    public async Task SELECT_42_returns_42()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);

            var repo = new ScalarRepo(fx.Connection);
            var result = await repo.AnswerAsync(CancellationToken.None).ConfigureAwait(false);

            result.Should().Be(42);
        }
    }
}
