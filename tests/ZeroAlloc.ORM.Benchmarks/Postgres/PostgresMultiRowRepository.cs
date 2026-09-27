using System.Data.Async;
using System.Runtime.CompilerServices;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Benchmarks.Postgres;

// ZA.ORM repository for PostgresMultiRowReadBench. One public type per file (MA0048).
public sealed partial class PostgresMultiRowRepository(IAsyncDbConnection connection)
{
    [Query("SELECT Id, CustomerId, Total FROM Orders ORDER BY Id")]
    public partial IAsyncEnumerable<OrderRow> StreamAllAsync(
        [EnumeratorCancellation] CancellationToken ct);
}
