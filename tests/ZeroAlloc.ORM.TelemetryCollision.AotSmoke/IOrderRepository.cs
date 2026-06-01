using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Telemetry;

namespace ZeroAlloc.ORM.TelemetryCollision.AotSmoke;

// ZA.Telemetry: [Instrument] on the interface emits a sealed proxy class
// (OrderRepositoryInstrumented) in this namespace, wrapping any IOrderRepository
// implementation. [Trace] / [Count] / [Histogram] choose what gets recorded.
[Instrument("ZeroAlloc.ORM.TelemetryCollision.AotSmoke")]
public interface IOrderRepository
{
    [Trace("orders.scalar")]
    [Count("orders.scalar_count")]
    Task<int> ScalarAsync(CancellationToken ct);

    [Trace("orders.get_by_id")]
    [Histogram("orders.get_by_id_ms")]
    Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
}
