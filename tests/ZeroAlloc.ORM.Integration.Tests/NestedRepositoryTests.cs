using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// Issue #238 — a repository nested inside a containing type (NestedScalarRepo.cs)
// must not just compile: the wrapped generated half has to actually run the
// query against a real connection. `dotnet build` alone would only prove the
// two partial halves joined; this proves the runtime path (open connection,
// execute, materialize) is unaffected by the extra nesting.
public class NestedRepositoryTests
{
    [Fact]
    public async Task Nested_repository_SELECT_42_returns_42()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);

            var repo = new NestedScalarContainer.ScalarRepository(fx.Connection);
            var result = await repo.AnswerAsync(CancellationToken.None).ConfigureAwait(false);

            result.Should().Be(42);
        }
    }
}
