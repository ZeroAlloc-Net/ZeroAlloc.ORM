using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

public sealed partial class FlexRowRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Id, OptionalCount, OptionalName FROM Flex ORDER BY Id LIMIT 1")]
    public partial Task<FlexRow?> GetFirstAsync(CancellationToken ct);
}
