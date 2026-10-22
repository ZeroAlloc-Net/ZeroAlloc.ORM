using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqCommand] deletes the way Daedalus' stores use EF's ExecuteDelete: by a status, by roles
// that are no longer configured, by a subquery, every row, and one row that must still be there.
// Plus [Command] inserts to seed them and a [LinqQuery] count to check them.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqCommandRepo(IAsyncDbConnection connection)
{
    [Command("""
        INSERT INTO "Tasks" ("Id", "ProjectId", "Title", "Status", "Kind", "CreatedAt", "Tags", "Order")
        VALUES (@id, @id, @title, @status, 'Bug', now(), '{}', 0)
        """)]
    public partial Task<int> InsertTaskAsync(Guid id, string title, WorkStatus status, CancellationToken ct);

    [Command("""
        INSERT INTO "Executions" ("Id", "TaskId", "InputTokens", "OutputTokens", "ExecutedAt")
        VALUES (@id, @taskId, 0, 0, now())
        """)]
    public partial Task<int> InsertExecutionAsync(Guid id, Guid taskId, CancellationToken ct);

    [LinqCommand(nameof(DeleteByStatusQuery))]
    public partial Task<int> DeleteByStatusAsync(WorkStatus status, CancellationToken ct);

    [LinqCommand(nameof(DeleteOtherTitlesQuery))]
    public partial ValueTask<int> DeleteOtherTitlesAsync(string[] titles, CancellationToken ct);

    [LinqCommand(nameof(DeleteExecutionsOfDoneQuery))]
    public partial Task<int> DeleteExecutionsOfDoneAsync(CancellationToken ct);

    [LinqCommand(nameof(DeleteAllQuery))]
    public partial ValueTask DeleteAllAsync(CancellationToken ct);

    [LinqCommand(nameof(DeleteByIdQuery), ThrowOnNoRows = true)]
    public partial Task DeleteExactlyAsync(Guid id, CancellationToken ct);

    [LinqCommand(nameof(DeleteByIdQuery))]
    public partial Task<int> DeleteByIdAsync(Guid id, IAsyncDbTransaction tx, CancellationToken ct);

    [LinqQuery(nameof(CountTasksQuery))]
    public partial Task<int> CountTasksAsync(CancellationToken ct);

    [LinqQuery(nameof(CountExecutionsQuery))]
    public partial Task<int> CountExecutionsAsync(CancellationToken ct);

    private static int DeleteByStatusQuery(WorkStatus status) =>
        Sql.Table<TaskEntity>().Where(t => t.Status == status).ExecuteDelete();

    private static int DeleteOtherTitlesQuery(string[] titles) =>
        Sql.Table<TaskEntity>().Where(t => !titles.Contains(t.Title)).ExecuteDelete();

    private static int DeleteExecutionsOfDoneQuery() =>
        Sql.Table<ExecutionEntity>()
            .Where(e => Sql.Table<TaskEntity>().Where(t => t.Status == WorkStatus.Done).Select(t => t.Id).Contains(e.TaskId))
            .ExecuteDelete();

    private static int DeleteAllQuery() => Sql.Table<TaskEntity>().ExecuteDelete();

    private static int DeleteByIdQuery(Guid id) => Sql.Table<TaskEntity>().Where(t => t.Id == id).ExecuteDelete();

    private static IQuery<int> CountTasksQuery() => Sql.Table<TaskEntity>().Count();

    private static IQuery<int> CountExecutionsQuery() => Sql.Table<ExecutionEntity>().Count();
}
