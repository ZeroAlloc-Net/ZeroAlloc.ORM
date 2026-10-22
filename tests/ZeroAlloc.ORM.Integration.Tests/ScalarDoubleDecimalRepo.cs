using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

internal sealed partial class ScalarDoubleDecimalRepo(IAsyncDbConnection connection)
{
    [Query("SELECT avg(x) FROM t")]
    public partial Task<double> AverageAsync(CancellationToken ct);

    [Query("SELECT sum(d) FROM t")]
    public partial Task<decimal> SumAsync(CancellationToken ct);

    [Query("SELECT avg(x) FROM t")]
    public partial ValueTask<double> AverageValueAsync(CancellationToken ct);

    [Query("SELECT sum(d) FROM t")]
    public partial ValueTask<decimal> SumValueAsync(CancellationToken ct);

    [Query("SELECT 1 WHERE 1 = 0")]
    public partial Task<double> NoRowDoubleAsync(CancellationToken ct);

    [Query("SELECT 1 WHERE 1 = 0")]
    public partial Task<decimal> NoRowDecimalAsync(CancellationToken ct);

    [Query("SELECT avg(x) FROM t WHERE 1 = 0")]
    public partial Task<double> NullDoubleAsync(CancellationToken ct);

    [Query("SELECT sum(d) FROM t WHERE 1 = 0")]
    public partial Task<decimal> NullDecimalAsync(CancellationToken ct);
}
