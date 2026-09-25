using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

internal sealed partial class StringParamRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Value FROM Things WHERE Name = @name")]
    public partial Task<int> GetByNameAsync(string name, CancellationToken ct);

    // v2.0, #235 — a `[Param]` facet on an input parameter is applied as written.
    // Microsoft.Data.Sqlite truncates a string value to a positive Size.
    [Query("SELECT Value FROM Things WHERE Name = @name")]
    public partial Task<int> GetByNameTruncatedAsync([Param(Size = 4)] string name, CancellationToken ct);
}
