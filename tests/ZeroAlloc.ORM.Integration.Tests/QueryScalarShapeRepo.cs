using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// #400 — non-nullable scalars and lists of a scalar from [Query]. The SQL runs unchanged
// on PostgreSQL and SQLite; QueryScalarShapeAssertions holds the schema per provider.
internal sealed partial class QueryScalarShapeRepo(IAsyncDbConnection connection)
{
    [Query("SELECT id FROM s WHERE k = @k")]
    public partial Task<Guid> GetIdAsync(int k, CancellationToken ct);

    [Query("SELECT at FROM s WHERE k = @k")]
    public partial ValueTask<DateTimeOffset> GetAtAsync(int k, CancellationToken ct);

    [Query("SELECT name FROM s WHERE k = @k")]
    public partial Task<string> GetNameAsync(int k, CancellationToken ct);

    [Query("SELECT name FROM s WHERE k <= @k ORDER BY k")]
    public partial Task<List<string>> GetNamesAsync(int k, CancellationToken ct);

    [Query("SELECT id FROM s WHERE k <= @k ORDER BY k")]
    public partial Task<IReadOnlyList<Guid>> GetIdsAsync(int k, CancellationToken ct);

    [Query("SELECT n FROM s WHERE k <= @k ORDER BY k")]
    public partial Task<IList<int?>> GetNumbersAsync(int k, CancellationToken ct);
}
