using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.AotSmoke;

public sealed partial class SmokeRepo(IAsyncDbConnection connection)
{
    [Query("SELECT 42")]
    public partial Task<int> ScalarAsync(CancellationToken ct);

    [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
    public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
}
