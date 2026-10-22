using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.AotSmoke;

// A page of parents with the children of exactly that page, under NativeAOT: a tuple companion
// whose child statement filters through an IN subquery, and whose parent reads owned columns.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqIncludeRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO smoke_tasks (id, title, origin_url, \"Origin_Branch\") VALUES (@id, @title, @url, @branch)")]
    public partial Task<int> InsertTaskAsync(Guid id, string title, string url, string? branch, CancellationToken ct);

    [LinqQuery(nameof(PagedTasksQuery))]
    public partial Task<(List<SmokeTaskHead> Tasks, List<SmokeRunLine> Runs)> PagedTasksWithRunsAsync(int skip, int take, CancellationToken ct);

    private static (IQuery<SmokeTaskHead>, IQuery<SmokeRunLine>) PagedTasksQuery(int skip, int take) =>
        (Sql.Table<SmokeTask>()
            .OrderBy(t => t.Title)
            .ThenBy(t => t.Id)
            .Skip(skip)
            .Take(take)
            .Select(t => new SmokeTaskHead(t.Id, t.Title, t.Origin!.Url, t.Origin!.Branch)),
         Sql.Table<SmokeRun>()
            .Where(r => Sql.Table<SmokeTask>()
                .OrderBy(t => t.Title)
                .ThenBy(t => t.Id)
                .Skip(skip)
                .Take(take)
                .Select(t => t.Id)
                .Contains(r.TaskId))
            .OrderBy(r => r.Tokens)
            .Select(r => new SmokeRunLine(r.TaskId, r.Tokens)));
}
