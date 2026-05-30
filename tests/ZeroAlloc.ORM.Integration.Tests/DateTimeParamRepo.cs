using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

internal sealed partial class DateTimeParamRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Value FROM Things WHERE Created = @created")]
    public partial Task<int> GetByCreatedAsync(DateTime created, CancellationToken ct);
}
