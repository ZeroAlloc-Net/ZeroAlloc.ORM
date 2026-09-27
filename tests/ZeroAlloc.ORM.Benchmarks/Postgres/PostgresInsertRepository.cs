using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Benchmarks.Postgres;

// ZA.ORM repository for PostgresInsertBench. One public type per file (MA0048).
public sealed partial class PostgresInsertRepository(IAsyncDbConnection connection)
{
    [Command("INSERT INTO Orders (Id, CustomerId, Total) VALUES (@id, @cust, @total)")]
    public partial Task<int> InsertAsync(int id, int cust, decimal total, CancellationToken ct);
}
