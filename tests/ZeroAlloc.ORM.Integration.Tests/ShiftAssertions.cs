using System.Data.Async;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// #256 — the round trip each provider's DateOnly and TimeOnly test runs once it
// has created the Shifts table.
internal static class ShiftAssertions
{
    public static readonly DateOnly Day = new(2024, 1, 2);

    // Microsecond precision, the finest a Postgres `time` keeps.
    public static readonly TimeOnly Start = new TimeOnly(13, 14, 15).Add(TimeSpan.FromTicks(1_234_560));

    public static readonly DateOnly End = new(2024, 12, 31);

    public static readonly TimeOnly Late = new TimeOnly(23, 59, 59).Add(TimeSpan.FromTicks(9_999_990));

    public static async Task RoundTripAsync(IAsyncDbConnection connection)
    {
        var repo = new ShiftRepo(connection);
        Assert.Equal(1, await repo.InsertAsync(1, Day, Start, End, Late, CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(1, await repo.InsertAsync(2, Day, Start, null, null, CancellationToken.None).ConfigureAwait(false));

        Assert.Equal(new ShiftRow(1, Day, Start, End, Late), await repo.GetAsync(1, CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(new ShiftRow(2, Day, Start, null, null), await repo.GetAsync(2, CancellationToken.None).ConfigureAwait(false));

        var found = await repo.FindAsync(Day, Start, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal([1, 2], found.Select(r => r.Id));

        Assert.Equal(Day, await repo.ShiftDayAsync(1, CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(Start, await repo.StartTimeAsync(1, CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(End, await repo.EndDayAsync(1, CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(Late, await repo.EndTimeAsync(1, CancellationToken.None).ConfigureAwait(false));
        Assert.Null(await repo.EndDayAsync(2, CancellationToken.None).ConfigureAwait(false));
        Assert.Null(await repo.EndTimeAsync(2, CancellationToken.None).ConfigureAwait(false));
    }
}
