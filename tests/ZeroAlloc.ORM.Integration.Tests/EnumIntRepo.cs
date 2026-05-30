using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

public sealed partial class EnumIntRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Id, Status FROM Items WHERE Status = @status LIMIT 1")]
    public partial Task<StatusRow?> GetByStatusAsync(Status status, CancellationToken ct);
}
