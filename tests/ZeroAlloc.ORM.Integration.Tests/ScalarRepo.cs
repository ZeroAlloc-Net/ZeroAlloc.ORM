using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

public sealed partial class ScalarRepo(IAsyncDbConnection connection)
{
    [Query("SELECT 42")]
    public partial Task<int> AnswerAsync(CancellationToken ct);
}
