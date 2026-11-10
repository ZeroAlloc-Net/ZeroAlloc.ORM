using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// #464 — TablePlaceholderRepo.CountByIdAsync with the table written into the SQL, the
// baseline its allocation gate compares against.
public sealed partial class TableLiteralRepo(IAsyncDbConnection connection)
{
    [Query("SELECT COUNT(*) FROM Widgets WHERE Id = @id")]
    public partial ValueTask<int> CountByIdAsync(int id, CancellationToken ct);
}
