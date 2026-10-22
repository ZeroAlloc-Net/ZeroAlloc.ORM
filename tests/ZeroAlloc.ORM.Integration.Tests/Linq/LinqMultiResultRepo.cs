using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] tuple companions: a parent with its children in one batch, the way Daedalus'
// TaskRepository and BrainstormRepository use EF's Include. Plus [Command] inserts to seed them.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqMultiResultRepo(IAsyncDbConnection connection)
{
    [Command("""
        INSERT INTO "Tasks" ("Id", "ProjectId", "Title", "Status", "Kind", "CreatedAt", "Tags", "Order")
        VALUES (@id, @projectId, @title, 0, 'Bug', now(), '{}', 0)
        """)]
    public partial Task<int> InsertTaskAsync(Guid id, Guid projectId, string title, CancellationToken ct);

    [Command("""
        INSERT INTO "Executions" ("Id", "TaskId", "InputTokens", "OutputTokens", "ExecutedAt")
        VALUES (@id, @taskId, @inputTokens, 0, @executedAt)
        """)]
    public partial Task<int> InsertExecutionAsync(Guid id, Guid taskId, int inputTokens, DateTime executedAt, CancellationToken ct);

    [Command("""INSERT INTO "BrainstormSessions" ("Id", "Topic") VALUES (@id, @topic)""")]
    public partial Task<int> InsertSessionAsync(Guid id, string topic, CancellationToken ct);

    [Command("""
        INSERT INTO "BrainstormMessages" ("Id", "SessionId", "Role", "Content", "CreatedAt")
        VALUES (@id, @sessionId, @role, @content, @createdAt)
        """)]
    public partial Task<int> InsertMessageAsync(Guid id, Guid sessionId, string role, string content, DateTimeOffset createdAt, CancellationToken ct);

    [LinqQuery(nameof(TaskWithExecutionsQuery))]
    public partial Task<(TaskHead? Head, List<ExecutionLine> Executions)> TaskWithExecutionsAsync(Guid id, CancellationToken ct);

    [LinqQuery(nameof(TaskWithExecutionsQuery))]
    public partial Task<(TaskHead? Head, List<ExecutionLine> Executions)?> TaskWithExecutionsOrNullAsync(Guid id, CancellationToken ct);

    [LinqQuery(nameof(TaskWithStatsQuery))]
    public partial Task<(TaskHead? Head, IReadOnlyList<ExecutionLine> Executions, int Count, double AverageInput)> TaskWithStatsAsync(Guid id, CancellationToken ct);

    [LinqQuery(nameof(TaskWithLargeExecutionsQuery))]
    public partial Task<(TaskHead? Head, List<ExecutionLine> Executions)> TaskWithLargeExecutionsAsync(Guid id, int minInputTokens, CancellationToken ct);

    [LinqQuery(nameof(SessionWithMessagesQuery))]
    public partial Task<(SessionHead? Session, List<MessageLine> Messages)> SessionWithMessagesAsync(Guid id, CancellationToken ct);

    private static (IQuery<TaskHead>, IQuery<ExecutionLine>) TaskWithExecutionsQuery(Guid id) =>
        (Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskHead(t.Id, t.Title)),
         Sql.Table<ExecutionEntity>()
            .Where(e => e.TaskId == id)
            .OrderBy(e => e.ExecutedAt)
            .Select(e => new ExecutionLine(e.Id, e.InputTokens)));

    private static (IQuery<TaskHead>, IQuery<ExecutionLine>, IQuery<int>, IQuery<double>) TaskWithStatsQuery(Guid id) =>
        (Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskHead(t.Id, t.Title)),
         Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).OrderBy(e => e.ExecutedAt).Select(e => new ExecutionLine(e.Id, e.InputTokens)),
         Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).Count(),
         Sql.Table<ExecutionEntity>().Where(e => e.TaskId == id).Average(e => e.InputTokens));

    // minInputTokens is read only by the child statement, so only its batch command binds it.
    private static (IQuery<TaskHead>, IQuery<ExecutionLine>) TaskWithLargeExecutionsQuery(Guid id, int minInputTokens) =>
        (Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskHead(t.Id, t.Title)),
         Sql.Table<ExecutionEntity>()
            .Where(e => e.TaskId == id && e.InputTokens >= minInputTokens)
            .OrderBy(e => e.ExecutedAt)
            .Select(e => new ExecutionLine(e.Id, e.InputTokens)));

    // BrainstormRepository: the session and its messages, oldest first.
    private static (IQuery<SessionHead>, IQuery<MessageLine>) SessionWithMessagesQuery(Guid id) =>
        (Sql.Table<BrainstormSessionEntity>().Where(s => s.Id == id).Select(s => new SessionHead(s.Id, s.Topic)),
         Sql.Table<BrainstormMessageEntity>()
            .Where(m => m.SessionId == id)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .Select(m => new MessageLine(m.Role, m.Content)));
}
