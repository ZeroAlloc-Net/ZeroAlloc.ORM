using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests.AllocationGates;

// #351 — the benchmark suite's single-statement workloads, for the allocation gates in
// SqliteWorkloadAllocationGateTests. The SQL matches tests/ZeroAlloc.ORM.Benchmarks.
[Dialect(SqlDialect.Sqlite)]
public sealed partial class SqliteWorkloadRepo(IAsyncDbConnection connection)
{
    [Query(WorkloadSql.SingleRowRead)]
    public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);

    [Command(WorkloadSql.Insert)]
    public partial Task<int> InsertAsync(int id, int cust, decimal total, CancellationToken ct);

    [Query(WorkloadSql.MultiResult, Batch = BatchMode.Auto)]
    public partial Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?> GetOrderWithLinesAsync(
        int id, CancellationToken ct);
}
