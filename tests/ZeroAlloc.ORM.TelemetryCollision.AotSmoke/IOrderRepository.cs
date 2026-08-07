using ZeroAlloc.Telemetry;

namespace ZeroAlloc.ORM.TelemetryCollision.AotSmoke;

// ZA.Telemetry-side of the collision smoke: an [Instrument]-annotated interface.
// The ZA.Telemetry generator emits OrderRepositoryInstrumented as a sealed class
// implementing IOrderRepository and forwarding to an inner instance.
//
// This file imports only ZeroAlloc.Telemetry; the ZA.ORM-side lives in
// OrderRepository.cs and imports only ZeroAlloc.ORM, matching the file-separation
// convention used by the ZA.Rest collision smoke.
[Instrument("ZeroAlloc.ORM.TelemetryCollision.AotSmoke")]
public interface IOrderRepository
{
    // Task<T?> is the shape that regressed: the proxy used to drop the nullable
    // annotation and emit Task<OrderRow>, which then failed to implement this
    // member (CS8613) and warned on the forwarded return (CS8603). Fixed upstream
    // in ZA.Telemetry 1.4.1 — this method is what keeps it fixed.
    [Trace("orders.get_by_id")]
    [TraceTagFromResult("orders.total", nameof(OrderRow.Total))]
    Task<OrderRow?> GetByIdAsync([TraceTag("orders.id")] int id, CancellationToken ct);

    // Non-nullable value result, so the proxy takes the plain member-access path
    // rather than the null-conditional one.
    [Trace("orders.scalar")]
    [Count("orders.scalar.calls")]
    Task<int> ScalarAsync(CancellationToken ct);
}
