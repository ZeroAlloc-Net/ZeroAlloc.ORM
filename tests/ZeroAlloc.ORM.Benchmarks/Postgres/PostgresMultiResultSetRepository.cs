using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Benchmarks.Postgres;

// ZA.ORM repository for PostgresMultiResultSetBench. One public type per file (MA0048).
[Dialect(SqlDialect.PostgreSql)]
public sealed partial class PostgresMultiResultSetRepository(IAsyncDbConnection connection)
{
    [Query(
        "SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Id, OrderId, Sku, Qty FROM OrderLines WHERE OrderId = @id;",
        Batch = BatchMode.Auto)]
    public partial Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?> GetOrderWithLinesAsync(
        int id,
        CancellationToken ct);

    // #427 — the same statements as one `;`-joined command, the Never mode, against the
    // NpgsqlBatch that Auto picks on Npgsql.
    [Query(
        "SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Id, OrderId, Sku, Qty FROM OrderLines WHERE OrderId = @id;",
        Batch = BatchMode.Never)]
    public partial Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?> GetOrderWithLinesJoinedAsync(
        int id,
        CancellationToken ct);
}
