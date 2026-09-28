using System.Data.Async;
using System.Runtime.CompilerServices;
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

    // Value-type shapes, see ZeroAlloc-Net/ZeroAlloc.Cache#182 and dotnet/runtime#134799.
    [Command("""
        INSERT INTO ValueRows (Id, At, Duration, Token, Payload, Priority, Status, Score, Ref, Sku, AltSku)
        VALUES (@id, @at, @duration, @token, @payload, @priority, @status, @score, @ref, @sku, @altSku)
        """)]
    public partial Task<int> InsertValueRowAsync(
        int id, DateTimeOffset at, TimeSpan duration, Guid token, byte[] payload, Priority priority,
        Status status, int? score, Guid? @ref, Sku sku, Sku? altSku, CancellationToken ct);

    [Query("SELECT Id, At, Duration, Token, Payload, Priority, Status, Score, Ref, Sku, AltSku FROM ValueRows WHERE Id = @id")]
    public partial Task<ValueRow?> GetValueRowAsync(int id, CancellationToken ct);

    [Query("SELECT Id, At, Duration, Token, Payload, Priority, Status, Score, Ref, Sku, AltSku FROM ValueRows WHERE Priority = @priority ORDER BY Id")]
    public partial Task<IReadOnlyList<ValueRow>> ListByPriorityAsync(Priority priority, CancellationToken ct);

    [Query("SELECT Id, At, Duration, Token, Payload, Priority, Status, Score, Ref, Sku, AltSku FROM ValueRows WHERE Status = @status ORDER BY Id")]
    public partial IAsyncEnumerable<ValueRow> StreamByStatusAsync(Status status, [EnumeratorCancellation] CancellationToken ct);

    [Command("SELECT Score FROM ValueRows WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<int?> ScoreAsync(int id, CancellationToken ct);

    [Command("SELECT Ref FROM ValueRows WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<Guid?> RefAsync(int id, CancellationToken ct);

    [Command("SELECT Priority FROM ValueRows WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<Priority> PriorityAsync(int id, CancellationToken ct);

    [Command("SELECT Status FROM ValueRows WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<Status> StatusAsync(int id, CancellationToken ct);

    [Command("SELECT Sku FROM ValueRows WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<Sku> SkuAsync(int id, CancellationToken ct);
}
