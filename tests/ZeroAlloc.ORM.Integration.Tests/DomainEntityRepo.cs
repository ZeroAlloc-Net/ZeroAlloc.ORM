using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

public sealed partial class DomainEntityRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
    public partial Task<OrderEntity?> GetByIdAsync(int id, CancellationToken ct);
}
