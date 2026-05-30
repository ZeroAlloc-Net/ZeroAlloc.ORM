using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

public sealed partial class ValueObjectRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Id, Name FROM Customers WHERE Id = @id")]
    public partial Task<CustomerRow?> GetAsync(CustomerId id, CancellationToken ct);
}
