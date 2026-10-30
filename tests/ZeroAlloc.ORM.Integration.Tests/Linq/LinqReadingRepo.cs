using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] and [LinqCommand] over columns of each DateTime storage: a parameter compared with
// or set into a declared column binds as that column's type, one compared with SeenAt as the
// type Npgsql infers from its Kind.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqReadingRepo(IAsyncDbConnection connection)
{
    [LinqQuery(nameof(TakenAtQuery))]
    public partial Task<List<ReadingId>> TakenAtAsync(DateTime at, CancellationToken ct);

    [LinqQuery(nameof(RecordedAtQuery))]
    public partial Task<List<ReadingId>> RecordedAtAsync(DateTime at, CancellationToken ct);

    [LinqQuery(nameof(SeenAtQuery))]
    public partial Task<List<ReadingId>> SeenAtAsync(DateTime at, CancellationToken ct);

    [LinqQuery(nameof(TakenUpToQuery))]
    public partial Task<List<ReadingId>> TakenUpToAsync(DateTime? until, CancellationToken ct);

    [LinqCommand(nameof(CloseQuery))]
    public partial Task<int> CloseAsync(Guid id, DateTime at, CancellationToken ct);

    [LinqCommand(nameof(CheckQuery))]
    public partial Task<int> CheckAsync(Guid id, DateTime at, CancellationToken ct);

    [LinqQuery(nameof(GetQuery))]
    public partial Task<ReadingLine?> GetAsync(Guid id, CancellationToken ct);

    private static IQuery<ReadingId> TakenAtQuery(DateTime at) =>
        Sql.Table<ReadingEntity>().Where(r => r.TakenAt == at).Select(r => new ReadingId(r.Id));

    private static IQuery<ReadingId> RecordedAtQuery(DateTime at) =>
        Sql.Table<ReadingEntity>().Where(r => r.RecordedAt == at).Select(r => new ReadingId(r.Id));

    private static IQuery<ReadingId> SeenAtQuery(DateTime at) =>
        Sql.Table<ReadingEntity>().Where(r => r.SeenAt == at).Select(r => new ReadingId(r.Id));

    // C#'s null semantics: a null `until` keeps every row, so the parameter is tested for NULL,
    // cast to the timestamp it binds as.
    private static IQuery<ReadingId> TakenUpToQuery(DateTime? until) =>
        Sql.Table<ReadingEntity>().Where(r => until == null || r.TakenAt <= until).Select(r => new ReadingId(r.Id));

    private static int CloseQuery(Guid id, DateTime at) =>
        Sql.Table<ReadingEntity>().Where(r => r.Id == id).ExecuteUpdate(set => set.SetProperty(r => r.ClosedAt, at));

    private static int CheckQuery(Guid id, DateTime at) =>
        Sql.Table<ReadingEntity>().Where(r => r.Id == id).ExecuteUpdate(set => set.SetProperty(r => r.CheckedAt, at));

    private static IQuery<ReadingLine> GetQuery(Guid id) =>
        Sql.Table<ReadingEntity>().Where(r => r.Id == id).Select(r => new ReadingLine(r.Id, r.ClosedAt, r.CheckedAt));
}
