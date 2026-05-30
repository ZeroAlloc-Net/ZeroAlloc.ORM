using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

internal sealed partial class DecimalParamRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Value FROM Things WHERE Price = @price")]
    public partial Task<int> GetByPriceAsync(decimal price, CancellationToken ct);
}
