using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.TelemetryCollision.AotSmoke;

// ZA.ORM: [Query] on partial methods. The generator fills the SQL-execution
// body. The class implements IOrderRepository so ZA.Telemetry's generated
// OrderRepositoryInstrumented proxy can wrap it.
public sealed partial class OrderRepository(IAsyncDbConnection connection)
    : IOrderRepository
{
    [Query("SELECT 42")]
    public partial Task<int> ScalarAsync(CancellationToken ct);

    [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
    public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
}
