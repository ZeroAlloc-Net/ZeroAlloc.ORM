using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Benchmarks;

// ZA.ORM repository for InsertBench. One public type per file (MA0048).
public sealed partial class InsertRepository(IAsyncDbConnection connection)
{
    [Command("INSERT INTO Orders (Id, CustomerId, Total) VALUES (@id, @cust, @total)")]
    public partial Task<int> InsertAsync(int id, int cust, decimal total, CancellationToken ct);
}
