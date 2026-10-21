using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.AotSmoke;

// [LinqQuery] under NativeAOT: the SQL is generated from each companion's chain at build time,
// and the companions never run. Program.cs runs these only when ZAORM_AOT_PG names a server.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO linq_items (id, name, rank, priority, status) VALUES (@id, @name, @rank, @priority, @status)")]
    public partial Task<int> InsertAsync(Guid id, string name, int rank, string priority, Status status, CancellationToken ct);

    [LinqQuery(nameof(ActiveQuery))]
    public partial Task<List<LinqItemRow>> ActiveAsync(int minRank, Priority? priority, CancellationToken ct);

    [LinqQuery(nameof(CountActiveQuery))]
    public partial Task<int> CountActiveAsync(int minRank, CancellationToken ct);

    private static IQuery<LinqItemRow> ActiveQuery(int minRank, Priority? priority) =>
        Sql.Table<LinqItem>()
            .Where(i => i.Rank >= minRank && i.Status == Status.Active)
            .Where(i => priority == null || i.Priority == priority)
            .OrderByDescending(i => i.Rank)
            .ThenBy(i => i.Name)
            .Select(i => new LinqItemRow(i.Id, i.Name, i.Priority));

    private static IQuery<int> CountActiveQuery(int minRank) =>
        Sql.Table<LinqItem>().Where(i => i.Rank >= minRank && i.Status == Status.Active).Count();
}
