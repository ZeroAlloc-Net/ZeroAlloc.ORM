using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.AotSmoke;

// A grouped inner join and a scalar Average under NativeAOT: both are translated at build time.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqJoinRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO smoke_tasks (id, title) VALUES (@id, @title)")]
    public partial Task<int> InsertTaskAsync(Guid id, string title, CancellationToken ct);

    [Command("INSERT INTO smoke_runs (id, task_id, tokens) VALUES (@id, @taskId, @tokens)")]
    public partial Task<int> InsertRunAsync(Guid id, Guid taskId, int tokens, CancellationToken ct);

    [LinqQuery(nameof(TokensPerTaskQuery))]
    public partial Task<List<TaskTokens>> TokensPerTaskAsync(CancellationToken ct);

    [LinqQuery(nameof(AverageTokensQuery))]
    public partial Task<double> AverageTokensAsync(CancellationToken ct);

    private static IQuery<TaskTokens> TokensPerTaskQuery() =>
        Sql.Table<SmokeRun>()
            .Join(Sql.Table<SmokeTask>(), r => r.TaskId, t => t.Id, (r, t) => new { Run = r, Task = t })
            .GroupBy(x => x.Task.Title)
            .OrderBy(g => g.Key)
            .Select(g => new TaskTokens(g.Key, g.Sum(x => (long)x.Run.Tokens), g.Count()));

    private static IQuery<double> AverageTokensQuery() =>
        Sql.Table<SmokeRun>().Average(r => r.Tokens);
}
