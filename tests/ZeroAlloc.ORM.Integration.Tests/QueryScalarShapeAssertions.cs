using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// #400 — the assertions QueryScalarShapeTests and PostgresQueryScalarShapeTests share.
// Row 1 has every column, row 2 a NULL n, row 3 a NULL name and at.
internal static class QueryScalarShapeAssertions
{
    public static readonly Guid Id1 = new("0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d01");
    public static readonly Guid Id2 = new("0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d02");
    public static readonly Guid Id3 = new("0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d03");
    public static readonly DateTimeOffset At1 = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static readonly string[] FirstTwoNames = ["alpha", "beta"];
    private static readonly Guid[] AllIds = [Id1, Id2, Id3];
    private static readonly int?[] AllNumbers = [10, null, 30];

    public const string PostgresSchema =
        "CREATE TABLE s (k INTEGER NOT NULL, id UUID NOT NULL, name TEXT NULL, at TIMESTAMPTZ NULL, n INTEGER NULL);"
        + " INSERT INTO s VALUES (1, '0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d01', 'alpha', '2026-01-02T03:04:05Z', 10);"
        + " INSERT INTO s VALUES (2, '0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d02', 'beta', '2026-01-02T03:04:05Z', NULL);"
        + " INSERT INTO s VALUES (3, '0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d03', NULL, NULL, 30);";

    // SQLite stores a Guid and a DateTimeOffset as TEXT, the way the ORM's own
    // parameters write them.
    public const string SqliteSchema =
        "CREATE TABLE s (k INTEGER NOT NULL, id TEXT NOT NULL, name TEXT NULL, at TEXT NULL, n INTEGER NULL);"
        + " INSERT INTO s VALUES (1, '0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d01', 'alpha', '2026-01-02 03:04:05+00:00', 10);"
        + " INSERT INTO s VALUES (2, '0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d02', 'beta', '2026-01-02 03:04:05+00:00', NULL);"
        + " INSERT INTO s VALUES (3, '0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d03', NULL, NULL, 30);";

    public static async Task ScalarsReadTheFirstColumnAsync(QueryScalarShapeRepo repo)
    {
        Assert.Equal(Id1, await repo.GetIdAsync(1, CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(At1, await repo.GetAtAsync(1, CancellationToken.None).ConfigureAwait(false));
        Assert.Equal("alpha", await repo.GetNameAsync(1, CancellationToken.None).ConfigureAwait(false));
    }

    public static async Task NoRowThrowsAsync(QueryScalarShapeRepo repo)
    {
        var id = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.GetIdAsync(99, CancellationToken.None)).ConfigureAwait(false);
        Assert.Equal(
            "Query 'ZeroAlloc.ORM.Integration.Tests.QueryScalarShapeRepo.GetIdAsync' returned no row, but its return type "
                + "is the non-nullable 'System.Guid'. Declare it as 'System.Guid?' to receive null.",
            id.Message);

        var at = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.GetAtAsync(99, CancellationToken.None).AsTask()).ConfigureAwait(false);
        Assert.Contains("returned no row", at.Message, StringComparison.Ordinal);

        var name = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.GetNameAsync(99, CancellationToken.None)).ConfigureAwait(false);
        Assert.Contains("returned no row", name.Message, StringComparison.Ordinal);
    }

    public static async Task NullThrowsAsync(QueryScalarShapeRepo repo)
    {
        var name = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.GetNameAsync(3, CancellationToken.None)).ConfigureAwait(false);
        Assert.Equal(
            "Query 'ZeroAlloc.ORM.Integration.Tests.QueryScalarShapeRepo.GetNameAsync' returned NULL, but its return type "
                + "is the non-nullable 'string'. Declare it as 'string?' to receive null.",
            name.Message);

        var at = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.GetAtAsync(3, CancellationToken.None).AsTask()).ConfigureAwait(false);
        Assert.Contains("returned NULL", at.Message, StringComparison.Ordinal);
    }

    public static async Task ListsReadEveryRowInOrderAsync(QueryScalarShapeRepo repo)
    {
        Assert.Equal(FirstTwoNames, await repo.GetNamesAsync(2, CancellationToken.None).ConfigureAwait(false), StringComparer.Ordinal);
        Assert.Equal(AllIds, await repo.GetIdsAsync(3, CancellationToken.None).ConfigureAwait(false));
    }

    public static async Task EmptyListsComeBackEmptyAsync(QueryScalarShapeRepo repo)
    {
        Assert.Empty(await repo.GetNamesAsync(0, CancellationToken.None).ConfigureAwait(false));
        Assert.Empty(await repo.GetIdsAsync(0, CancellationToken.None).ConfigureAwait(false));
        Assert.Empty(await repo.GetNumbersAsync(0, CancellationToken.None).ConfigureAwait(false));
    }

    public static async Task NullIntoANonNullableElementThrowsAsync(QueryScalarShapeRepo repo)
    {
        var names = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.GetNamesAsync(3, CancellationToken.None)).ConfigureAwait(false);
        Assert.Equal(
            "Query 'ZeroAlloc.ORM.Integration.Tests.QueryScalarShapeRepo.GetNamesAsync' returned NULL at row index 2, "
                + "but its list element type is the non-nullable 'string'. Declare the element as 'string?' to receive null.",
            names.Message);
    }

    public static async Task NullableElementsKeepNullsAsync(QueryScalarShapeRepo repo)
    {
        Assert.Equal(AllNumbers, await repo.GetNumbersAsync(3, CancellationToken.None).ConfigureAwait(false));
    }
}
