using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.AotSmoke;

public sealed partial class SmokeRepo(IAsyncDbConnection connection)
{
    [Query("SELECT 42")]
    public partial Task<int> ScalarAsync(CancellationToken ct);

    [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
    public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);

    // #256 — DateOnly and TimeOnly as parameters, row columns and scalar results.
    [Command("INSERT INTO Shifts (Id, Day, Start, EndDay) VALUES (@id, @day, @start, @endDay)")]
    public partial Task<int> InsertShiftAsync(int id, DateOnly day, TimeOnly start, DateOnly? endDay, CancellationToken ct);

    [Query("SELECT Id, Day, Start, EndDay FROM Shifts WHERE Id = @id")]
    public partial Task<ShiftRow?> GetShiftAsync(int id, CancellationToken ct);

    [Command("SELECT Start FROM Shifts WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<TimeOnly> ShiftStartAsync(int id, CancellationToken ct);
}
