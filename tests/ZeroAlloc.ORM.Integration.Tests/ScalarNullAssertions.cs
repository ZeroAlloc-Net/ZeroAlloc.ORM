using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// #250 — the NULL-scalar contract, shared by the Sqlite, SQL Server and Postgres
// test classes so each provider is held to the same messages.
internal static class ScalarNullAssertions
{
    public static async Task NonNullableTargetsThrowAsync(ScalarNullRepo repo)
    {
        var text = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.NullStringAsync(CancellationToken.None)).ConfigureAwait(false);
        Assert.Equal(
            "Scalar command 'ZeroAlloc.ORM.Integration.Tests.ScalarNullRepo.NullStringAsync' returned NULL, " +
            "but its return type is the non-nullable 'string'. Declare it as 'string?' to receive null.",
            text.Message);

        var number = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.NullIntAsync(CancellationToken.None)).ConfigureAwait(false);
        Assert.Equal(
            "Scalar command 'ZeroAlloc.ORM.Integration.Tests.ScalarNullRepo.NullIntAsync' returned NULL, " +
            "but its return type is the non-nullable 'int'. Declare it as 'int?' to receive null.",
            number.Message);

        var wrapped = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.NullValueObjectAsync(CancellationToken.None)).ConfigureAwait(false);
        Assert.Contains("non-nullable 'ZeroAlloc.ORM.Integration.Tests.TotalAmount'", wrapped.Message, StringComparison.Ordinal);

        var status = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.NullEnumAsync(CancellationToken.None)).ConfigureAwait(false);
        Assert.Equal(
            "Scalar command 'ZeroAlloc.ORM.Integration.Tests.ScalarNullRepo.NullEnumAsync' returned NULL, " +
            "but its return type is the non-nullable 'ZeroAlloc.ORM.Integration.Tests.Status'. " +
            "Declare it as 'ZeroAlloc.ORM.Integration.Tests.Status?' to receive null.",
            status.Message);

        var identity = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.NullIdentityAsync(CancellationToken.None)).ConfigureAwait(false);
        Assert.Equal(
            "Identity command 'ZeroAlloc.ORM.Integration.Tests.ScalarNullRepo.NullIdentityAsync' returned NULL, " +
            "but its return type is the non-nullable 'int'. The SQL must produce a non-null identity value.",
            identity.Message);
    }

    public static async Task NullableTargetsReceiveNullAsync(ScalarNullRepo repo)
    {
        Assert.Null(await repo.NullableStringAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Null(await repo.NullableIntAsync(CancellationToken.None).ConfigureAwait(false));
    }
}
