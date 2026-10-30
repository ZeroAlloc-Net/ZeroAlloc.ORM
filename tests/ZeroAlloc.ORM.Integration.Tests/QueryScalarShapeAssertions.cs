using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// #400 — the assertions QueryScalarShapeTests and PostgresQueryScalarShapeTests share.
// Row 1 has every column, row 2 a NULL n, row 3 a NULL name, at, st and sl. #410 — st
// holds an int enum, sl a [StoreAsString] one.
internal static class QueryScalarShapeAssertions
{
    public static readonly Guid Id1 = new("0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d01");
    public static readonly Guid Id2 = new("0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d02");
    public static readonly Guid Id3 = new("0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d03");
    public static readonly DateTimeOffset At1 = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static readonly string[] FirstTwoNames = ["alpha", "beta"];
    private static readonly Guid[] AllIds = [Id1, Id2, Id3];
    private static readonly int?[] AllNumbers = [10, null, 30];
    private static readonly Status[] FirstTwoStatuses = [Status.Cancelled, Status.Pending];
    private static readonly Status?[] AllStatuses = [Status.Cancelled, Status.Pending, null];
    private static readonly StringStatus?[] AllStringStatuses = [StringStatus.Cancelled, StringStatus.Pending, null];
    private static readonly string?[] AllNames = ["alpha", "beta", null];

    public const string PostgresSchema =
        "CREATE TABLE s (k INTEGER NOT NULL, id UUID NOT NULL, name TEXT NULL, at TIMESTAMPTZ NULL, n INTEGER NULL,"
        + " st INTEGER NULL, sl TEXT NULL);"
        + " INSERT INTO s VALUES (1, '0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d01', 'alpha', '2026-01-02T03:04:05Z', 10, 1, 'Cancelled');"
        + " INSERT INTO s VALUES (2, '0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d02', 'beta', '2026-01-02T03:04:05Z', NULL, 0, 'Pending');"
        + " INSERT INTO s VALUES (3, '0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d03', NULL, NULL, 30, NULL, NULL);";

    // SQLite stores a Guid and a DateTimeOffset as TEXT, the way the ORM's own
    // parameters write them.
    public const string SqliteSchema =
        "CREATE TABLE s (k INTEGER NOT NULL, id TEXT NOT NULL, name TEXT NULL, at TEXT NULL, n INTEGER NULL,"
        + " st INTEGER NULL, sl TEXT NULL);"
        + " INSERT INTO s VALUES (1, '0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d01', 'alpha', '2026-01-02 03:04:05+00:00', 10, 1, 'Cancelled');"
        + " INSERT INTO s VALUES (2, '0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d02', 'beta', '2026-01-02 03:04:05+00:00', NULL, 0, 'Pending');"
        + " INSERT INTO s VALUES (3, '0b6f3a52-1c1d-4f7e-9a3b-2f1f6c3e8d03', NULL, NULL, 30, NULL, NULL);";

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

    // #410 — an enum reads through its convention: a cast from the integer column, or
    // Enum.Parse of the member name for [StoreAsString]. A nullable enum maps no row
    // and NULL to null.
    public static async Task EnumScalarsReadTheFirstColumnAsync(QueryScalarShapeRepo repo)
    {
        Assert.Equal(Status.Cancelled, await repo.GetStatusAsync(1, CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(Status.Pending, await repo.GetStatusAsync(2, CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(Status.Cancelled, await repo.GetMaybeStatusAsync(1, CancellationToken.None).ConfigureAwait(false));
        Assert.Null(await repo.GetMaybeStatusAsync(3, CancellationToken.None).ConfigureAwait(false));
        Assert.Null(await repo.GetMaybeStatusAsync(99, CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(StringStatus.Cancelled, await repo.GetStringStatusAsync(1, CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(StringStatus.Pending, await repo.GetMaybeStringStatusAsync(2, CancellationToken.None).ConfigureAwait(false));
        Assert.Null(await repo.GetMaybeStringStatusAsync(3, CancellationToken.None).ConfigureAwait(false));
        Assert.Null(await repo.GetMaybeStringStatusAsync(99, CancellationToken.None).ConfigureAwait(false));
    }

    public static async Task NonNullableEnumThrowsOnNoRowAndNullAsync(QueryScalarShapeRepo repo)
    {
        var noRow = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.GetStatusAsync(99, CancellationToken.None)).ConfigureAwait(false);
        Assert.Equal(
            "Query 'ZeroAlloc.ORM.Integration.Tests.QueryScalarShapeRepo.GetStatusAsync' returned no row, but its return type "
                + "is the non-nullable 'ZeroAlloc.ORM.Integration.Tests.Status'. "
                + "Declare it as 'ZeroAlloc.ORM.Integration.Tests.Status?' to receive null.",
            noRow.Message);

        var nullValue = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.GetStringStatusAsync(3, CancellationToken.None)).ConfigureAwait(false);
        Assert.Equal(
            "Query 'ZeroAlloc.ORM.Integration.Tests.QueryScalarShapeRepo.GetStringStatusAsync' returned NULL, but its return type "
                + "is the non-nullable 'ZeroAlloc.ORM.Integration.Tests.StringStatus'. "
                + "Declare it as 'ZeroAlloc.ORM.Integration.Tests.StringStatus?' to receive null.",
            nullValue.Message);
    }

    public static async Task EnumListsReadEveryRowAsync(QueryScalarShapeRepo repo)
    {
        Assert.Equal(FirstTwoStatuses, await repo.GetStatusesAsync(2, CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(AllStringStatuses, await repo.GetMaybeStringStatusesAsync(3, CancellationToken.None).ConfigureAwait(false));
        Assert.Empty(await repo.GetStatusesAsync(0, CancellationToken.None).ConfigureAwait(false));

        var nullElement = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.GetStatusesAsync(3, CancellationToken.None)).ConfigureAwait(false);
        Assert.Contains("returned NULL at row index 2", nullElement.Message, StringComparison.Ordinal);
    }

    // #410 — a tuple element that is a list of a scalar reads column 0 of every row of
    // its result set, keeping NULL for a nullable element.
    public static async Task TupleScalarListsReadEveryRowAsync(QueryScalarShapeRepo repo)
    {
        var all = await repo.GetSummaryAsync(3, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(3L, all.Count);
        Assert.Equal(AllNames, all.Names, StringComparer.Ordinal);
        Assert.Equal(AllStatuses, all.Statuses);

        var none = await repo.GetSummaryAsync(0, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(0L, none.Count);
        Assert.Empty(none.Names);
        Assert.Empty(none.Statuses);

        var firstTwo = await repo.GetCountAndNamesAsync(2, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(2L, firstTwo.Count);
        Assert.Equal(FirstTwoNames, firstTwo.Names, StringComparer.Ordinal);
    }

    public static async Task NullIntoANonNullableTupleListElementThrowsAsync(QueryScalarShapeRepo repo)
    {
        var names = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(
            () => repo.GetCountAndNamesAsync(3, CancellationToken.None)).ConfigureAwait(false);
        Assert.Equal(
            "Column 'name' is NULL, but an element of tuple element 'Names' is the non-nullable type 'string'. "
                + "Declare it as 'string?' to receive null.",
            names.Message);
    }
}
