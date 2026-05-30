using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

public sealed partial class LifecycleRepo(IAsyncDbConnection connection)
{
    [Query("SELECT 42")]
    public partial Task<int> AnswerAsync(CancellationToken ct);
}
