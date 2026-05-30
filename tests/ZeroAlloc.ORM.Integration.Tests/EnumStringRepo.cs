using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

public sealed partial class EnumStringRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Id, Status FROM Items WHERE Status = @status LIMIT 1")]
    public partial Task<StringStatusRow?> GetByStatusAsync(StringStatus status, CancellationToken ct);
}
