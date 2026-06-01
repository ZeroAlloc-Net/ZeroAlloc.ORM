using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.GeneratorCollision.AotSmoke;

// ZA.ORM-side of the collision smoke: a partial class with [Query]-annotated
// partial methods. The generator emits the materialization pipeline against
// IAsyncDbConnection. This file imports only the ZA.ORM namespace — see
// IOrderApi.cs for the ZA.Rest-side, which imports only the ZA.Rest namespace.
// Keeping the `using` directives apart sidesteps the QueryAttribute name
// collision (ZA.Rest has a parameter-level Query; ZA.ORM has a method-level
// Query) without preventing the two generators from emitting into the same
// assembly.
public sealed partial class SmokeRepo(IAsyncDbConnection connection)
{
    [Query("SELECT 42")]
    public partial Task<int> ScalarAsync(CancellationToken ct);

    [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
    public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
}
