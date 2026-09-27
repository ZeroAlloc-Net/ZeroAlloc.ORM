using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// #256 — DateOnly and TimeOnly as parameters, row columns and scalar results.
// The SQL is the same on every provider; each test creates the Shifts table with
// the provider's own column types: TEXT on Sqlite, `date` and `time` on Postgres
// and SQL Server.
public sealed partial class ShiftRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO Shifts (Id, ShiftDay, StartTime, EndDay, EndTime) VALUES (@id, @shiftDay, @startTime, @endDay, @endTime)")]
    public partial Task<int> InsertAsync(
        int id, DateOnly shiftDay, TimeOnly startTime, DateOnly? endDay, TimeOnly? endTime, CancellationToken ct);

    [Query("SELECT Id, ShiftDay, StartTime, EndDay, EndTime FROM Shifts WHERE Id = @id")]
    public partial Task<ShiftRow?> GetAsync(int id, CancellationToken ct);

    // Parameters in a WHERE clause: they must compare equal to the stored values.
    [Query("SELECT Id, ShiftDay, StartTime, EndDay, EndTime FROM Shifts WHERE ShiftDay = @shiftDay AND StartTime = @startTime ORDER BY Id")]
    public partial Task<IReadOnlyList<ShiftRow>> FindAsync(DateOnly shiftDay, TimeOnly startTime, CancellationToken ct);

    [Command("SELECT ShiftDay FROM Shifts WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<DateOnly> ShiftDayAsync(int id, CancellationToken ct);

    [Command("SELECT StartTime FROM Shifts WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<TimeOnly> StartTimeAsync(int id, CancellationToken ct);

    [Command("SELECT EndDay FROM Shifts WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<DateOnly?> EndDayAsync(int id, CancellationToken ct);

    [Command("SELECT EndTime FROM Shifts WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<TimeOnly?> EndTimeAsync(int id, CancellationToken ct);
}
