using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Row locks the way Daedalus takes them with EF Core's raw FOR UPDATE: a task locked before it
// changes, a work queue each worker claims from with SKIP LOCKED, and a lock that fails at once
// with NOWAIT. Each locking method takes the transaction its lock lasts for. Plus [Command]
// inserts to seed rows and a [LinqCommand] rename, the second writer.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqLockRepo(IAsyncDbConnection connection)
{
    [Command("""
        INSERT INTO "Tasks" ("Id", "ProjectId", "Title", "Status", "Kind", "CreatedAt", "Tags", "Order")
        VALUES (@id, @id, @title, 0, 'Bug', now(), '{}', @order)
        """)]
    public partial Task<int> InsertTaskAsync(Guid id, string title, int order, CancellationToken ct);

    [LinqQuery(nameof(LockByIdQuery))]
    public partial Task<TaskLine?> LockAsync(Guid id, IAsyncDbTransaction tx, CancellationToken ct);

    [LinqQuery(nameof(NoWaitByIdQuery))]
    public partial Task<TaskLine?> LockNoWaitAsync(Guid id, IAsyncDbTransaction tx, CancellationToken ct);

    [LinqQuery(nameof(ClaimQuery))]
    public partial Task<List<TaskLine>> ClaimAsync(WorkStatus status, int count, IAsyncDbTransaction tx, CancellationToken ct);

    [LinqQuery(nameof(TitleQuery))]
    public partial Task<TaskLine?> GetAsync(Guid id, CancellationToken ct);

    [LinqCommand(nameof(RenameQuery))]
    public partial Task<int> RenameAsync(Guid id, string title, CancellationToken ct);

    private static IQuery<TaskLine> LockByIdQuery(Guid id) =>
        Sql.Table<TaskEntity>().Where(t => t.Id == id).ForUpdate().Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> NoWaitByIdQuery(Guid id) =>
        Sql.Table<TaskEntity>().Where(t => t.Id == id).ForUpdateNoWait().Select(t => new TaskLine(t.Id, t.Title, t.Order));

    // The first `count` open tasks no other transaction holds.
    private static IQuery<TaskLine> ClaimQuery(WorkStatus status, int count) =>
        Sql.Table<TaskEntity>()
            .Where(t => t.Status == status)
            .OrderBy(t => t.Order)
            .Take(count)
            .ForUpdateSkipLocked()
            .Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> TitleQuery(Guid id) =>
        Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static int RenameQuery(Guid id, string title) =>
        Sql.Table<TaskEntity>().Where(t => t.Id == id).ExecuteUpdate(set => set.SetProperty(t => t.Title, title));
}
