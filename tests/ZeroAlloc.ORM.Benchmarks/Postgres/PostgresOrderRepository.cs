using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Benchmarks.Postgres;

// ZA.ORM repository for PostgresSingleRowReadBench. One public type per file (MA0048).
public sealed partial class PostgresOrderRepository(IAsyncDbConnection connection)
{
    [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
    public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
}
