using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// Nullable parameter case — the SQL uses `IS NULL` because SQL's three-valued
// logic makes `Column = NULL` always-false, so a `= @id` shape would not return
// the seeded row when `id` is null. The parameter still binds (DBNull.Value via
// the null-guard); the test verifies the emit doesn't NRE on a null argument.
internal sealed partial class NullableIntParamRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Value FROM Things WHERE @id IS NULL")]
    public partial Task<int> GetWhenNullAsync(int? id, CancellationToken ct);
}
