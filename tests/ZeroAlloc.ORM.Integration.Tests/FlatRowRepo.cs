using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

public sealed partial class FlatRowRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Id, CustomerId, Total FROM Orders ORDER BY Id LIMIT 1")]
    public partial Task<OrderRow?> GetFirstAsync(CancellationToken ct);
}
