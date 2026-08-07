using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.TelemetryCollision.AotSmoke;

// ZA.ORM-side of the collision smoke: a partial class with [Query]-annotated
// partial methods, implementing the [Instrument]-annotated interface. The ORM
// generator emits the materialization pipeline against IAsyncDbConnection; the
// Telemetry generator emits a proxy wrapping this type through the interface.
//
// Both generators therefore emit into the same assembly, against the same
// signatures — including the Task<OrderRow?> that surfaced the upstream
// nullable-annotation bug. This file imports only ZeroAlloc.ORM; see
// IOrderRepository.cs for the ZA.Telemetry-side.
public sealed partial class OrderRepository(IAsyncDbConnection connection) : IOrderRepository
{
    [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
    public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);

    [Query("SELECT 42")]
    public partial Task<int> ScalarAsync(CancellationToken ct);
}
