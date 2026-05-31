using System.Data.Async;
using System.Runtime.CompilerServices;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

public sealed partial class StreamingRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Id, CustomerId, Total FROM Orders ORDER BY Id")]
    public partial IAsyncEnumerable<OrderRow> StreamAllAsync(
        [EnumeratorCancellation] CancellationToken ct);
}
