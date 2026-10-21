using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] Where clauses over the Daedalus-shaped schema. Seeded through LinqRepo.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqWhereRepo(IAsyncDbConnection connection)
{
    [Command("""UPDATE "Tasks" SET "StartedAt" = @at WHERE "Id" = @id""")]
    public partial Task<int> StartAsync(Guid id, DateTime at, CancellationToken ct);

    [Command("""UPDATE "Tasks" SET "Priority" = @priority, "Rating" = @rating, "Category" = @category WHERE "Id" = @id""")]
    public partial Task<int> GradeAsync(Guid id, int? priority, float? rating, WorkKind? category, CancellationToken ct);

    [LinqQuery(nameof(MinOrderIfQuery))]
    public partial Task<List<TaskLine>> MinOrderIfAsync(int? minOrder, CancellationToken ct);

    [LinqQuery(nameof(DueQuery))]
    public partial Task<List<TaskLine>> DueAsync(DateTimeOffset? due, CancellationToken ct);

    [LinqQuery(nameof(NotDueQuery))]
    public partial Task<List<TaskLine>> NotDueAsync(DateTimeOffset? due, CancellationToken ct);

    [LinqQuery(nameof(NotFeatureQuery))]
    public partial Task<List<TaskLine>> NotFeatureAsync(CancellationToken ct);

    [LinqQuery(nameof(OtherCategoryQuery))]
    public partial Task<List<TaskLine>> OtherCategoryAsync(WorkKind? category, CancellationToken ct);

    [LinqQuery(nameof(OtherPriorityQuery))]
    public partial Task<List<TaskLine>> OtherPriorityAsync(int priority, CancellationToken ct);

    [LinqQuery(nameof(NotAboveQuery))]
    public partial Task<List<TaskLine>> NotAboveAsync(int priority, CancellationToken ct);

    [LinqQuery(nameof(RatedQuery))]
    public partial Task<List<TaskLine>> RatedAsync(CancellationToken ct);

    [LinqQuery(nameof(KindUnlessBugQuery))]
    public partial Task<List<TaskLine>> KindUnlessBugAsync(WorkKind kind, CancellationToken ct);

    [LinqQuery(nameof(AmongOptionalQuery))]
    public partial Task<List<TaskLine>> AmongOptionalAsync(Guid[]? ids, CancellationToken ct);

    [LinqQuery(nameof(AmongOptionalOrQuery))]
    public partial Task<List<TaskLine>> AmongOptionalOrAsync(Guid[]? ids, CancellationToken ct);

    [LinqQuery(nameof(StartedBeforeQuery))]
    public partial Task<List<TaskLine>> StartedBeforeAsync(DateTime? before, CancellationToken ct);

    [LinqQuery(nameof(StartedBeforeOrQuery))]
    public partial Task<List<TaskLine>> StartedBeforeOrAsync(DateTime? before, CancellationToken ct);

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

    [LinqQuery(nameof(ProjectValueQuery))]
    public partial Task<List<TaskLine>> ProjectValueAsync(Guid project, CancellationToken ct);

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

    // A null minOrder makes the condition null, which C# reads as false: the filter is skipped.
    private static IQuery<TaskLine> MinOrderIfQuery(int? minOrder) =>
        Sql.Table<TaskEntity>().WhereIf(minOrder > 0, t => t.Order >= minOrder).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    // Both sides can be null, so a null due matches the rows without one, as in C#.
    private static IQuery<TaskLine> DueQuery(DateTimeOffset? due) =>
        Sql.Table<TaskEntity>().Where(t => t.DueAt == due).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> NotDueQuery(DateTimeOffset? due) =>
        Sql.Table<TaskEntity>().Where(t => t.DueAt != due).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    // A null category is not Feature, so its rows are kept.
    private static IQuery<TaskLine> NotFeatureQuery() =>
        Sql.Table<TaskEntity>().Where(t => t.Category != WorkKind.Feature).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> OtherCategoryQuery(WorkKind? category) =>
        Sql.Table<TaskEntity>().Where(t => t.Category != category).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> OtherPriorityQuery(int priority) =>
        Sql.Table<TaskEntity>().Where(t => t.Priority != priority).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    // C# reads `null > p` as false, so its negation keeps the rows without a priority.
    private static IQuery<TaskLine> NotAboveQuery(int priority) =>
        Sql.Table<TaskEntity>().Where(t => !(t.Priority > priority)).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    // A float literal compares as real, the column's type, not as a double.
    private static IQuery<TaskLine> RatedQuery() =>
        Sql.Table<TaskEntity>().Where(t => t.Rating == 0.1f).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    // The constant is compared with a parameter that binds by name, so it is written by name.
    private static IQuery<TaskLine> KindUnlessBugQuery(WorkKind kind) =>
        Sql.Table<TaskEntity>().WhereIf(kind != WorkKind.Bug, t => t.Kind == kind).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    // Arrays and DateTime have no DbType facet: their null tests carry a cast.
    private static IQuery<TaskLine> AmongOptionalQuery(Guid[]? ids) =>
        Sql.Table<TaskEntity>().WhereIf(ids != null, t => ids!.Contains(t.Id)).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> AmongOptionalOrQuery(Guid[]? ids) =>
        Sql.Table<TaskEntity>().Where(t => ids == null || ids.Contains(t.Id)).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> StartedBeforeQuery(DateTime? before) =>
        Sql.Table<TaskEntity>().WhereIf(before != null, t => t.StartedAt < before).Select(t => new TaskLine(t.Id, t.Title, t.Order));

    private static IQuery<TaskLine> StartedBeforeOrQuery(DateTime? before) =>
        Sql.Table<TaskEntity>().Where(t => before == null || t.StartedAt < before).Select(t => new TaskLine(t.Id, t.Title, t.Order));

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

    // A value object's .Value is its column, compared with a Guid.
    private static IQuery<TaskLine> ProjectValueQuery(Guid project) =>
        Sql.Table<TaskEntity>()
            .Where(t => t.ProjectId.Value == project)
            .OrderBy(t => t.Order)
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
