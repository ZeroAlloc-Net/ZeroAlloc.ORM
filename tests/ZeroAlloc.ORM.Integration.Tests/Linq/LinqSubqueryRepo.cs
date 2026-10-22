using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] tuple companions that load a page of parents with the children of exactly that
// page: the child statement filters through an IN subquery that repeats the parent statement's
// order and page, the way Daedalus' TaskQueryService and ProjectQueryService use EF's Include on
// a paged list. Plus [Command] inserts to seed them.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqSubqueryRepo(IAsyncDbConnection connection)
{
    [Command("""
        INSERT INTO "Tasks" ("Id", "ProjectId", "Title", "Status", "Kind", "CreatedAt", "Tags", "Order")
        VALUES (@id, @projectId, @title, 0, 'Bug', @createdAt, '{}', 0)
        """)]
    public partial Task<int> InsertTaskAsync(Guid id, Guid projectId, string title, DateTimeOffset createdAt, CancellationToken ct);

    [Command("""
        INSERT INTO "Executions" ("Id", "TaskId", "InputTokens", "OutputTokens", "ExecutedAt")
        VALUES (@id, @taskId, @inputTokens, 0, @executedAt)
        """)]
    public partial Task<int> InsertExecutionAsync(Guid id, Guid taskId, int inputTokens, DateTime executedAt, CancellationToken ct);

    [Command("""INSERT INTO app."Projects" ("Id", "Name", "Order") VALUES (@id, @name, @order)""")]
    public partial Task<int> InsertProjectAsync(Guid id, string name, int order, CancellationToken ct);

    [LinqQuery(nameof(PagedTasksQuery))]
    public partial Task<(List<TaskHead> Tasks, List<ExecutionOfTask> Executions)> PagedTasksWithExecutionsAsync(int skip, int take, CancellationToken ct);

    [LinqQuery(nameof(PagedProjectsQuery))]
    public partial Task<(List<ProjectLine> Projects, List<ProjectTask> Tasks)> PagedProjectsWithTasksAsync(int skip, int take, CancellationToken ct);

    // TaskQueryService: newest tasks first, Id breaking ties, and the executions of that page.
    private static (IQuery<TaskHead>, IQuery<ExecutionOfTask>) PagedTasksQuery(int skip, int take) =>
        (Sql.Table<TaskEntity>()
            .OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Skip(skip)
            .Take(take)
            .Select(t => new TaskHead(t.Id, t.Title)),
         Sql.Table<ExecutionEntity>()
            .Where(e => Sql.Table<TaskEntity>()
                .OrderByDescending(t => t.CreatedAt)
                .ThenBy(t => t.Id)
                .Skip(skip)
                .Take(take)
                .Select(t => t.Id)
                .Contains(e.TaskId))
            .OrderBy(e => e.ExecutedAt)
            .ThenBy(e => e.Id)
            .Select(e => new ExecutionOfTask(e.Id, e.TaskId, e.InputTokens)));

    // ProjectQueryService: projects in their Order, and the tasks of that page. The task's
    // ProjectId is a typed id, whose Value is its column.
    private static (IQuery<ProjectLine>, IQuery<ProjectTask>) PagedProjectsQuery(int skip, int take) =>
        (Sql.Table<ProjectEntity>()
            .OrderBy(p => p.Order)
            .ThenBy(p => p.Id)
            .Skip(skip)
            .Take(take)
            .Select(p => new ProjectLine(p.Id, p.Order, p.Title)),
         Sql.Table<TaskEntity>()
            .Where(t => Sql.Table<ProjectEntity>()
                .OrderBy(p => p.Order)
                .ThenBy(p => p.Id)
                .Skip(skip)
                .Take(take)
                .Select(p => p.Id)
                .Contains(t.ProjectId.Value))
            .OrderBy(t => t.Title)
            .ThenBy(t => t.Id)
            .Select(t => new ProjectTask(t.Id, t.ProjectId.Value, t.Title)));
}
