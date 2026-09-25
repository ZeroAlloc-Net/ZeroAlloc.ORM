using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// #250 — NULL scalar results on Postgres. See ScalarNullRepo.
[Trait("Provider", "Postgres")]
public sealed class PostgresScalarNullTests
{
    [Fact]
    public async Task Null_into_a_non_nullable_scalar_throws()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await ScalarNullAssertions.NonNullableTargetsThrowAsync(new ScalarNullRepo(fx.Connection)).ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_into_a_nullable_scalar_returns_null()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await ScalarNullAssertions.NullableTargetsReceiveNullAsync(new ScalarNullRepo(fx.Connection)).ConfigureAwait(false);
    }
}
