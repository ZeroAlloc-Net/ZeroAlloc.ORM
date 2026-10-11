using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// #331 - the same query declared with both return types, so a test can measure
// what the Task costs on a provider that completes synchronously.
public sealed partial class ValueTaskQueryRepo(IAsyncDbConnection connection)
{
    // 42, not a small number: the runtime caches completed Task<int> instances
    // for -1 to 8, which would hide the Task this test measures.
    [Query("SELECT 42")]
    public partial Task<int> AnswerAsTaskAsync(CancellationToken ct);

    [Query("SELECT 42")]
    public partial ValueTask<int> AnswerAsync(CancellationToken ct);
}
