using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

internal sealed partial class GuidParamRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Value FROM Things WHERE Guid = @guid")]
    public partial Task<int> GetByGuidAsync(Guid guid, CancellationToken ct);
}
