using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

internal sealed partial class StringParamRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Value FROM Things WHERE Name = @name")]
    public partial Task<int> GetByNameAsync(string name, CancellationToken ct);
}
