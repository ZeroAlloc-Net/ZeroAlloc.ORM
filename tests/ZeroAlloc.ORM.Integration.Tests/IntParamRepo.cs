using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

internal sealed partial class IntParamRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Value FROM Things WHERE Id = @id")]
    public partial Task<int> GetByIdAsync(int id, CancellationToken ct);
}
