using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] ordering, keyset paging and the Count, LongCount and Any terminals over the
// Daedalus-shaped schema. Seeded through LinqRepo, or through the transaction insert here.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqOrderPagingRepo(IAsyncDbConnection connection)
{
    [Command("""
        INSERT INTO "Tasks" ("Id", "ProjectId", "Title", "Status", "Kind", "CreatedAt", "Tags", "Order")
        VALUES (@id, @projectId, @title, 0, 'Bug', @createdAt, '{}', @order)
        """)]
    public partial Task<int> InsertInTransactionAsync(
        Guid id,
        Guid projectId,
        string title,
        DateTimeOffset createdAt,
        int order,
        IAsyncDbTransaction tx,
        CancellationToken ct);

    [LinqQuery(nameof(FirstPageQuery))]
    public partial Task<List<KeysetRow>> FirstPageAsync(int take, CancellationToken ct);

    [LinqQuery(nameof(NextPageQuery))]
    public partial Task<List<KeysetRow>> NextPageAsync(DateTimeOffset afterAt, Guid afterId, int take, CancellationToken ct);

    [LinqQuery(nameof(OffsetPageQuery))]
    public partial Task<List<TaskLine>> OffsetPageAsync(int skip, int take, CancellationToken ct);

    [LinqQuery(nameof(CountAtLeastQuery))]
    public partial Task<int> CountAtLeastAsync(int min, CancellationToken ct);

    [LinqQuery(nameof(CountKindQuery))]
    public partial ValueTask<long> CountKindAsync(WorkKind kind, CancellationToken ct);

    [LinqQuery(nameof(AnyTitledQuery))]
    public partial Task<bool> AnyTitledAsync(string title, CancellationToken ct);

    [LinqQuery(nameof(CountPageQuery))]
    public partial Task<int> CountPageAsync(int min, int skip, int take, CancellationToken ct);

    [LinqQuery(nameof(AnyPageQuery))]
    public partial Task<bool> AnyPageAsync(int min, int skip, CancellationToken ct);

    [LinqQuery(nameof(CountAtLeastQuery))]
    public partial Task<int> CountAtLeastInTransactionAsync(int min, IAsyncDbTransaction tx, CancellationToken ct);

    [LinqQuery(nameof(TitledQuery))]
    public partial Task<List<TaskLine>> TitledInTransactionAsync(string title, IAsyncDbTransaction tx, CancellationToken ct);

    private static IQuery<KeysetRow> FirstPageQuery(int take) =>
        Sql.Table<TaskEntity>()
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Take(take)
            .Select(t => new KeysetRow(t.Id, t.CreatedAt));

    private static IQuery<KeysetRow> NextPageQuery(DateTimeOffset afterAt, Guid afterId, int take) =>
        Sql.Table<TaskEntity>()
            .Where(t => Sql.Row(t.CreatedAt, t.Id) > Sql.Row(afterAt, afterId))
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Take(take)
            .Select(t => new KeysetRow(t.Id, t.CreatedAt));

    private static IQuery<TaskLine> OffsetPageQuery(int skip, int take) =>
        Sql.Table<TaskEntity>()
            .OrderByDescending(t => t.Order)
            .Skip(skip)
            .Take(take)
            .Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<int> CountAtLeastQuery(int min) =>
        Sql.Table<TaskEntity>().Where(t => t.Order >= min).Count();

    private static IQuery<long> CountKindQuery(WorkKind kind) =>
        Sql.Table<TaskEntity>().Where(t => t.Kind == kind).Select(t => new TaskLine(t.Id, t.Title, t.Order)).LongCount();

    private static IQuery<bool> AnyTitledQuery(string title) =>
        Sql.Table<TaskEntity>().Where(t => string.Equals(t.Title, title, StringComparison.Ordinal)).Any();

    private static IQuery<int> CountPageQuery(int min, int skip, int take) =>
        Sql.Table<TaskEntity>().Where(t => t.Order >= min).OrderBy(t => t.Order).Skip(skip).Take(take).Count();

    private static IQuery<bool> AnyPageQuery(int min, int skip) =>
        Sql.Table<TaskEntity>().Where(t => t.Order >= min).Skip(skip).Any();

    private static IQuery<TaskLine> TitledQuery(string title) =>
        Sql.Table<TaskEntity>().Where(t => string.Equals(t.Title, title, StringComparison.Ordinal)).Select(t => new TaskLine(t.Id, t.Title, t.Order));
}
