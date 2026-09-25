using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// Issue #238 — runtime coverage for a repository nested inside a containing
// type. Container itself carries no ORM members; it exists purely to prove the
// generator's wrapped emit compiles AND runs correctly end to end against a
// real (Sqlite) connection, not just that the generated source parses.
public partial class NestedScalarContainer
{
    public sealed partial class ScalarRepository(IAsyncDbConnection connection)
    {
        [Query("SELECT 42")]
        public partial Task<int> AnswerAsync(CancellationToken ct);
    }
}
