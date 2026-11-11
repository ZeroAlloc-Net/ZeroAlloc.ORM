using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// The table is a settable property: members are read on every call, so a change is honoured.
public sealed partial class ChangingTableRepo(IAsyncDbConnection connection)
{
    public string? Table { get; set; }

    [Query("SELECT COUNT(*) FROM {table}")]
    public partial Task<int> CountAsync(CancellationToken ct);
}
