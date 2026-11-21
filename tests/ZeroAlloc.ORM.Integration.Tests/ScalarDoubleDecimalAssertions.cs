using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// #409 — a non-nullable double or decimal [Query] throws
// ZeroAllocOrmMaterializationException for no row and for a NULL aggregate, as
// Task<int> does, on SQLite and Postgres alike.
internal static class ScalarDoubleDecimalAssertions
{
    public static async Task NoRowThrowsAsync(ScalarDoubleDecimalRepo repo)
    {
        var noDouble = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.NoRowDoubleAsync(CancellationToken.None)).ConfigureAwait(false);
        Assert.Equal(
            "Query 'ZeroAlloc.ORM.Integration.Tests.ScalarDoubleDecimalRepo.NoRowDoubleAsync' returned no row, " +
            "but its return type is the non-nullable 'double'. Declare it as 'double?' to receive null.",
            noDouble.Message);
        var noDecimal = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.NoRowDecimalAsync(CancellationToken.None)).ConfigureAwait(false);
        Assert.Equal(
            "Query 'ZeroAlloc.ORM.Integration.Tests.ScalarDoubleDecimalRepo.NoRowDecimalAsync' returned no row, " +
            "but its return type is the non-nullable 'decimal'. Declare it as 'decimal?' to receive null.",
            noDecimal.Message);
    }

    public static async Task NullAggregateThrowsAsync(ScalarDoubleDecimalRepo repo)
    {
        var nullDouble = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.NullDoubleAsync(CancellationToken.None)).ConfigureAwait(false);
        Assert.Equal(
            "Query 'ZeroAlloc.ORM.Integration.Tests.ScalarDoubleDecimalRepo.NullDoubleAsync' returned NULL, " +
            "but its return type is the non-nullable 'double'. Declare it as 'double?' to receive null.",
            nullDouble.Message);
        var nullDecimal = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.NullDecimalAsync(CancellationToken.None)).ConfigureAwait(false);
        Assert.Equal(
            "Query 'ZeroAlloc.ORM.Integration.Tests.ScalarDoubleDecimalRepo.NullDecimalAsync' returned NULL, " +
            "but its return type is the non-nullable 'decimal'. Declare it as 'decimal?' to receive null.",
            nullDecimal.Message);
    }
}
