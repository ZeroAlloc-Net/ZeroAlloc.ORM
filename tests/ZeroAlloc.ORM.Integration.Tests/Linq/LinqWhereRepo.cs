using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] Where clauses over the Daedalus-shaped schema. Seeded through LinqRepo.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqWhereRepo(IAsyncDbConnection connection)
{
    [LinqQuery(nameof(RangeQuery))]
    public partial Task<List<TaskLine>> RangeAsync(int min, int max, CancellationToken ct);

    [LinqQuery(nameof(ExactQuery))]
    public partial Task<List<TaskLine>> ExactAsync(int order, string title, CancellationToken ct);

    [LinqQuery(nameof(WithoutDueQuery))]
    public partial Task<List<TaskLine>> WithoutDueAsync(CancellationToken ct);

    [LinqQuery(nameof(WithDueQuery))]
    public partial Task<List<TaskLine>> WithDueAsync(CancellationToken ct);

    [LinqQuery(nameof(TypedQuery))]
    public partial Task<List<TaskLine>> TypedAsync(ProjectId project, WorkStatus status, WorkKind kind, CancellationToken ct);

    [LinqQuery(nameof(FeaturesQuery))]
    public partial Task<List<TaskLine>> FeaturesAsync(CancellationToken ct);

    [LinqQuery(nameof(AmongQuery))]
    public partial Task<List<TaskLine>> AmongAsync(Guid[] ids, CancellationToken ct);

    [LinqQuery(nameof(ContainingQuery))]
    public partial Task<List<TaskLine>> ContainingAsync(string text, CancellationToken ct);

    [LinqQuery(nameof(StartingQuery))]
    public partial Task<List<TaskLine>> StartingAsync(string prefix, CancellationToken ct);

    [LinqQuery(nameof(EndingQuery))]
    public partial Task<List<TaskLine>> EndingAsync(string suffix, CancellationToken ct);

    [LinqQuery(nameof(LikeQuery))]
    public partial Task<List<TaskLine>> LikeAsync(string pattern, CancellationToken ct);

    [LinqQuery(nameof(OptionalQuery))]
    public partial Task<List<TaskLine>> OptionalAsync(
        int? order,
        WorkStatus? status,
        Guid? id,
        DateTimeOffset? createdBefore,
        ProjectId? project,
        WorkKind? kind,
        CancellationToken ct);

    [LinqQuery(nameof(OptionalOrQuery))]
    public partial Task<List<TaskLine>> OptionalOrAsync(
        int? order,
        WorkStatus? status,
        Guid? id,
        DateTimeOffset? createdBefore,
        ProjectId? project,
        WorkKind? kind,
        CancellationToken ct);

    [LinqQuery(nameof(ReusedQuery))]
    public partial Task<List<TaskLine>> ReusedAsync(int? minOrder, string? title, CancellationToken ct);

    private static IQuery<TaskLine> RangeQuery(int min, int max) =>
        Sql.Table<TaskEntity>().Where(t => t.Order >= min && t.Order < max).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> ExactQuery(int order, string title) =>
        Sql.Table<TaskEntity>().Where(t => t.Order == order || !string.Equals(t.Title, title, StringComparison.Ordinal)).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> WithoutDueQuery() =>
        Sql.Table<TaskEntity>().Where(t => t.DueAt == null).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> WithDueQuery() =>
        Sql.Table<TaskEntity>().Where(t => t.DueAt != null).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> TypedQuery(ProjectId project, WorkStatus status, WorkKind kind) =>
        Sql.Table<TaskEntity>()
            .Where(t => t.ProjectId == project)
            .Where(t => t.Status == status && t.Kind == kind)
            .Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> FeaturesQuery() =>
        Sql.Table<TaskEntity>()
            .Where(t => t.Kind == WorkKind.Feature && t.Status != WorkStatus.Done)
            .Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> AmongQuery(Guid[] ids) =>
        Sql.Table<TaskEntity>().Where(t => ids.Contains(t.Id)).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> ContainingQuery(string text) =>
        Sql.Table<TaskEntity>().Where(t => t.Title.Contains(text)).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> StartingQuery(string prefix) =>
        Sql.Table<TaskEntity>().Where(t => t.Title.StartsWith(prefix, StringComparison.Ordinal)).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> EndingQuery(string suffix) =>
        Sql.Table<TaskEntity>().Where(t => t.Title.EndsWith(suffix, StringComparison.Ordinal)).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> LikeQuery(string pattern) =>
        Sql.Table<TaskEntity>().Where(t => Sql.ILike(t.Title, pattern)).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    // Each optional filter only in its WhereIf condition and predicate: a null must type.
    private static IQuery<TaskLine> OptionalQuery(
        int? order, WorkStatus? status, Guid? id, DateTimeOffset? createdBefore, ProjectId? project, WorkKind? kind) =>
        Sql.Table<TaskEntity>()
            .WhereIf(order != null, t => t.Order == order)
            .WhereIf(status != null, t => t.Status == status)
            .WhereIf(id != null, t => t.Id == id)
            .WhereIf(createdBefore != null, t => t.CreatedAt < createdBefore)
            .WhereIf(project != null, t => t.ProjectId == project)
            .WhereIf(kind != null, t => t.Kind == kind)
            .Select(t => new TaskLine(t.Id, t.Title, t.Order));

    // The same filters written as `x == null || column == x` inside Where.
    private static IQuery<TaskLine> OptionalOrQuery(
        int? order, WorkStatus? status, Guid? id, DateTimeOffset? createdBefore, ProjectId? project, WorkKind? kind) =>
        Sql.Table<TaskEntity>()
            .Where(t => order == null || t.Order == order)
            .Where(t => status == null || t.Status == status)
            .Where(t => id == null || t.Id == id)
            .Where(t => createdBefore == null || t.CreatedAt < createdBefore)
            .Where(t => project == null || t.ProjectId == project)
            .Where(t => kind == null || t.Kind == kind)
            .Select(t => new TaskLine(t.Id, t.Title, t.Order));

    // minOrder appears in the WhereIf condition, its predicate and a second Where.
    private static IQuery<TaskLine> ReusedQuery(int? minOrder, string? title) =>
        Sql.Table<TaskEntity>()
            .WhereIf(minOrder != null, t => t.Order >= minOrder)
            .Where(t => minOrder == null || t.Order >= minOrder)
            .WhereIf(title != null, t => string.Equals(t.Title, title, StringComparison.Ordinal) || t.Title.StartsWith(title!, StringComparison.Ordinal))
            .Select(t => new TaskLine(t.Id, t.Title, t.Order));
}
