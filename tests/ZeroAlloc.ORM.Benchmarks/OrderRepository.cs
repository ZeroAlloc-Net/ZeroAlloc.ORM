using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Benchmarks;

// ZA.ORM repository — generator emits the GetByIdAsync body. Uses the same
// primary-constructor connection-injection convention as the integration tests
// (the generator discovers the IAsyncDbConnection parameter automatically).
// One public type per file (MA0048).
public sealed partial class OrderRepository(IAsyncDbConnection connection)
{
    [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
    public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
}
