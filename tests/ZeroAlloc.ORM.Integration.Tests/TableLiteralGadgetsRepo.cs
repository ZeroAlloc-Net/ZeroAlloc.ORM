using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// The same literal method against the second table, for the alternating-tables gate.
public sealed partial class TableLiteralGadgetsRepo(IAsyncDbConnection connection)
{
    [Query("SELECT COUNT(*) FROM Gadgets WHERE Id = @id")]
    public partial ValueTask<int> CountByIdAsync(int id, CancellationToken ct);
}
