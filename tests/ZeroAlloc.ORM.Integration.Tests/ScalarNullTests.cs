using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// #250 — NULL scalar results on Sqlite. See ScalarNullRepo.
public sealed class ScalarNullTests
{
    [Fact]
    public async Task Null_into_a_non_nullable_scalar_throws()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await ScalarNullAssertions.NonNullableTargetsThrowAsync(new ScalarNullRepo(fx.Connection)).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Null_into_a_nullable_scalar_returns_null()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await ScalarNullAssertions.NullableTargetsReceiveNullAsync(new ScalarNullRepo(fx.Connection)).ConfigureAwait(false);
        }
    }
}
