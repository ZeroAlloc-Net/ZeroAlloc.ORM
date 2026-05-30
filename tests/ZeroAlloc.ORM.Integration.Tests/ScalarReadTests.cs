using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

public class ScalarReadTests
{
    [Fact]
    public async Task SELECT_42_returns_42()
    {
        await using var fx = new SqliteFixture();
        await fx.InitializeAsync();

        var repo = new ScalarRepo(fx.Connection);
        var result = await repo.AnswerAsync(CancellationToken.None);

        result.Should().Be(42);
    }
}
