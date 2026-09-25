using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.SqlServer;

// #250 — NULL scalar results on SQL Server 2022. See ScalarNullRepo.
public sealed class SqlServerScalarNullTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fx = new();

    public ValueTask InitializeAsync() => _fx.InitializeAsync();
    public ValueTask DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public Task Null_into_a_non_nullable_scalar_throws()
        => ScalarNullAssertions.NonNullableTargetsThrowAsync(new ScalarNullRepo(_fx.Connection));

    [Fact]
    public Task No_row_into_a_non_nullable_scalar_throws()
        => ScalarNullAssertions.NoRowIntoNonNullableTargetsThrowsAsync(new ScalarNullRepo(_fx.Connection));

    [Fact]
    public Task Null_into_a_nullable_scalar_returns_null()
        => ScalarNullAssertions.NullableTargetsReceiveNullAsync(new ScalarNullRepo(_fx.Connection));
}
