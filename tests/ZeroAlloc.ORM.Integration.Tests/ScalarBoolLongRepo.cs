using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

internal sealed partial class ScalarBoolLongRepo(IAsyncDbConnection connection)
{
    [Query("SELECT EXISTS (SELECT 1 FROM t WHERE id = @id)")]
    public partial Task<bool> ExistsAsync(int id, CancellationToken ct);

    [Query("SELECT count(*) FROM t")]
    public partial Task<long> CountAsync(CancellationToken ct);

    [Query("SELECT 1 WHERE 1 = 0")]
    public partial Task<bool> NoRowBoolAsync(CancellationToken ct);

    [Query("SELECT 1 WHERE 1 = 0")]
    public partial Task<long> NoRowLongAsync(CancellationToken ct);
}
