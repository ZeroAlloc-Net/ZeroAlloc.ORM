using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// v0.3 Phase E — round-trip integration coverage for MultiResultSet emit.
// Mirrors StreamingRepo's connection-injection convention (primary ctor field).
// Three methods cover the matrix:
//   * GetOrderWithLinesAutoAsync  — (Head, Lines) tuple via BatchMode.Auto (runtime branch).
//   * GetOrderWithLinesNeverAsync — same shape via BatchMode.Never (forced ;-joined).
//   * GetCountFirstAllAsync       — 3-element (Scalar, Row, List) tuple via BatchMode.Auto.
public sealed partial class MultiResultSetRepo(IAsyncDbConnection connection)
{
    [Query(
        "SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Id, OrderId, Sku, Qty FROM OrderLines WHERE OrderId = @id;",
        Batch = BatchMode.Auto)]
    public partial Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?> GetOrderWithLinesAutoAsync(
        int id,
        CancellationToken ct);

    [Query(
        "SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Id, OrderId, Sku, Qty FROM OrderLines WHERE OrderId = @id;",
        Batch = BatchMode.Never)]
    public partial Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?> GetOrderWithLinesNeverAsync(
        int id,
        CancellationToken ct);

    [Query(
        "SELECT COUNT(*) FROM Orders; SELECT Id, CustomerId, Total FROM Orders ORDER BY Id LIMIT 1; SELECT Id, CustomerId, Total FROM Orders ORDER BY Id;",
        Batch = BatchMode.Auto)]
    public partial Task<(int Count, OrderRow First, IReadOnlyList<OrderRow> All)?> GetCountFirstAllAsync(
        CancellationToken ct);
}
