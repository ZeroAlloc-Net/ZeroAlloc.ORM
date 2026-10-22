using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] inner joins over Executions, Tasks and Projects: Daedalus' cost reports.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqJoinRepo(IAsyncDbConnection connection)
{
    [LinqQuery(nameof(CostsByProjectQuery))]
    public partial Task<List<ProjectModelCost>> CostsByProjectAsync(DateTime since, CancellationToken ct);

    [LinqQuery(nameof(CostsByTaskQuery))]
    public partial Task<List<TaskCost>> CostsByTaskAsync(ProjectId projectId, CancellationToken ct);

    [LinqQuery(nameof(ExecutionTitlesQuery))]
    public partial Task<List<ExecutionTitle>> ExecutionTitlesAsync(CancellationToken ct);

    [LinqQuery(nameof(TaskProjectIdsQuery))]
    public partial Task<List<TaskProjectIds>> TaskProjectIdsAsync(CancellationToken ct);

    // Daedalus Q3: tokens per project and model since a moment, through two joins; the
    // project's Title is the "Name" column of app."Projects".
    private static IQuery<ProjectModelCost> CostsByProjectQuery(DateTime since) =>
        Sql.Table<ExecutionEntity>()
            .Where(e => e.ExecutedAt >= since)
            .Join(Sql.Table<TaskEntity>(), e => e.TaskId, t => t.Id, (e, t) => new { Execution = e, Task = t })
            .Join(Sql.Table<ProjectEntity>(), et => et.Task.ProjectId.Value, p => p.Id, (et, p) => new { et.Execution, et.Task, Project = p })
            .GroupBy(x => new { x.Project.Id, x.Project.Title, x.Execution.ModelId })
            .OrderBy(g => g.Key.Title)
            .ThenBy(g => g.Key.ModelId)
            .Select(g => new ProjectModelCost(
                g.Key.Id,
                g.Key.Title,
                g.Key.ModelId,
                g.Sum(x => (long)x.Execution.InputTokens),
                g.Sum(x => (long)x.Execution.OutputTokens),
                g.Count()));

    // Daedalus Q4: tokens per task of one project; the inner source is filtered by the parameter.
    private static IQuery<TaskCost> CostsByTaskQuery(ProjectId projectId) =>
        Sql.Table<ExecutionEntity>()
            .Join(
                Sql.Table<TaskEntity>().Where(t => t.ProjectId == projectId),
                e => e.TaskId,
                t => t.Id,
                (e, t) => new { Execution = e, Task = t })
            .GroupBy(x => new { x.Task.Id, x.Task.Title })
            .OrderBy(g => g.Key.Title)
            .Select(g => new TaskCost(g.Key.Id, g.Key.Title, g.Sum(x => (long)x.Execution.InputTokens), g.Count()));

    private static IQuery<ExecutionTitle> ExecutionTitlesQuery() =>
        Sql.Table<ExecutionEntity>()
            .Join(Sql.Table<TaskEntity>(), e => e.TaskId, t => t.Id, (e, t) => new { e, t })
            .OrderBy(x => x.t.Title)
            .ThenBy(x => x.e.InputTokens)
            .Select(x => new ExecutionTitle(x.t.Title, x.e.InputTokens));

    private static IQuery<TaskProjectIds> TaskProjectIdsQuery() =>
        Sql.Table<TaskEntity>()
            .Join(Sql.Table<ProjectEntity>(), t => t.ProjectId.Value, p => p.Id, (t, p) => new { Task = t, Project = p })
            .OrderBy(x => x.Project.Title)
            .Select(x => new TaskProjectIds(x.Task.Id, x.Project.Id, x.Project.Title));
}
