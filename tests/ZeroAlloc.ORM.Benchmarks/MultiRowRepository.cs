using System.Data.Async;
using System.Runtime.CompilerServices;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Benchmarks;

// ZA.ORM repository for MultiRowReadBench. One public type per file (MA0048).
public sealed partial class MultiRowRepository(IAsyncDbConnection connection)
{
    [Query("SELECT Id, CustomerId, Total FROM Orders ORDER BY Id")]
    public partial IAsyncEnumerable<OrderRow> StreamAllAsync(
        [EnumeratorCancellation] CancellationToken ct);
}
