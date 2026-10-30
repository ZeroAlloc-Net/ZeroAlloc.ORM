using System.Data.Async;
using System.Runtime.CompilerServices;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] methods over the Daedalus-shaped schema, plus [Command] inserts to seed it.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqRepo(IAsyncDbConnection connection)
{
    [Command("""
        INSERT INTO "Tasks" ("Id", "ProjectId", "Title", "Status", "Kind", "CreatedAt", "DueAt", "Tags", "Order")
        VALUES (@id, @projectId, @title, @status, @kind, @createdAt, @dueAt, @tags, @order)
        """)]
    public partial Task<int> InsertTaskAsync(
        Guid id,
        ProjectId projectId,
        string title,
        WorkStatus status,
        string kind,
        DateTimeOffset createdAt,
        DateTimeOffset? dueAt,
        string[] tags,
        int order,
        CancellationToken ct);

    [Command("""INSERT INTO app."Projects" ("Id", "Name", "Order") VALUES (@id, @name, @order)""")]
    public partial Task<int> InsertProjectAsync(Guid id, string name, int order, CancellationToken ct);

    [LinqQuery(nameof(ProjectionsQuery))]
    public partial Task<List<TaskProjection>> ProjectionsAsync(CancellationToken ct);

    [LinqQuery(nameof(ProjectionsQuery))]
    public partial Task<TaskProjection?> FirstProjectionAsync(CancellationToken ct);

    [LinqQuery(nameof(ProjectionsQuery))]
    public partial IAsyncEnumerable<TaskProjection> StreamProjectionsAsync([EnumeratorCancellation] CancellationToken ct);

    [LinqQuery(nameof(LinesQuery))]
    public partial Task<IReadOnlyList<TaskLine>> LinesAsync(CancellationToken ct);

    [LinqQuery(nameof(ProjectLinesQuery))]
    public partial Task<IList<ProjectLine>> ProjectLinesAsync(CancellationToken ct);

    [LinqQuery(nameof(LiteralsQuery))]
    public partial Task<List<LiteralRow>> LiteralsAsync(string label, CancellationToken ct);

    [LinqQuery(nameof(StampedQuery))]
    public partial Task<List<StampRow>> StampedAsync(DateTime? at, CancellationToken ct);

    [LinqQuery(nameof(StampedBeforeQuery))]
    public partial Task<List<StampRow>> StampedBeforeAsync(DateTime? at, CancellationToken ct);

    [LinqQuery(nameof(TitlesQuery))]
    public partial Task<List<string>> TitlesAsync(int min, CancellationToken ct);

    [LinqQuery(nameof(KindsQuery))]
    public partial Task<IReadOnlyList<WorkKind>> KindsAsync(CancellationToken ct);

    [LinqQuery(nameof(DueDatesQuery))]
    public partial Task<IList<DateTimeOffset?>> DueDatesAsync(CancellationToken ct);

    [LinqQuery(nameof(StatusesQuery))]
    public partial Task<List<WorkStatus>> StatusesAsync(CancellationToken ct);

    [LinqQuery(nameof(ProjectIdsQuery))]
    public partial Task<List<ProjectId>> ProjectIdsAsync(CancellationToken ct);

    // #429 — a value-type typed id at [Query] scalar position reads through its From factory.
    [Query("""SELECT "ProjectId" FROM "Tasks" WHERE "Order" = @order""")]
    public partial Task<ProjectId> ProjectIdOfAsync(int order, CancellationToken ct);

    [Query("""SELECT "ProjectId" FROM "Tasks" WHERE "Order" = @order""")]
    public partial Task<ProjectId?> MaybeProjectIdOfAsync(int order, CancellationToken ct);

    // One value per row, read as a list of it.
    private static IQuery<string> TitlesQuery(int min) =>
        Sql.Table<TaskEntity>().Where(t => t.Order >= min).OrderBy(t => t.Order).Select(t => t.Title);

    // Kind is stored by name, so each value is parsed.
    private static IQuery<WorkKind> KindsQuery() =>
        Sql.Table<TaskEntity>().OrderBy(t => t.Order).Select(t => t.Kind);

    private static IQuery<DateTimeOffset?> DueDatesQuery() =>
        Sql.Table<TaskEntity>().OrderBy(t => t.Order).Select(t => t.DueAt);

    private static IQuery<ProjectId> ProjectIdsQuery() =>
        Sql.Table<TaskEntity>().OrderBy(t => t.Order).Select(t => t.ProjectId);

    private static IQuery<WorkStatus> StatusesQuery() =>
        Sql.Table<TaskEntity>().GroupBy(t => t.Status).OrderBy(g => g.Key).Select(g => g.Key);

    private static IQuery<TaskProjection> ProjectionsQuery() =>
        Sql.Table<TaskEntity>().Select(t => new TaskProjection(t.Id, t.ProjectId, t.Status, t.Kind, t.Tags, t.DueAt));

    // Named arguments in another order than the constructor's: each value must still reach its parameter.
    private static IQuery<TaskLine> LinesQuery() =>
        Sql.Table<TaskEntity>().Select(t => new TaskLine(Order: t.Order, Title: t.Title, Id: t.Id));

    private static IQuery<ProjectLine> ProjectLinesQuery() =>
        Sql.Table<ProjectEntity>().Select(p => new ProjectLine(p.Id, p.Order, p.Title));

    private static IQuery<LiteralRow> LiteralsQuery(string label) =>
        Sql.Table<TaskEntity>().Select(t => new LiteralRow(t.Id, label, 42, 0.5, null, Origin.Imported));

    // DateTime binds without a DbType facet, so a null is typed by a cast in the SELECT list.
    private static IQuery<StampRow> StampedQuery(DateTime? at) =>
        Sql.Table<TaskEntity>().Select(t => new StampRow(t.Id, at));

    // The SELECT list is read first, so the cast there also types the parameter for the WHERE.
    private static IQuery<StampRow> StampedBeforeQuery(DateTime? at) =>
        Sql.Table<TaskEntity>().Where(t => at == null || t.StartedAt < at).Select(t => new StampRow(t.Id, at));
}
