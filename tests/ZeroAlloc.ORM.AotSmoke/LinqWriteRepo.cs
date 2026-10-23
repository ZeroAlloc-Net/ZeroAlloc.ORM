using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.AotSmoke;

// [LinqCommand] writes and a [LinqQuery] row lock under NativeAOT: an update guarded by xmin that
// throws when the row changed, a delete, and a SKIP LOCKED claim inside a transaction. Program.cs
// runs these only when ZAORM_AOT_PG names a server.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqWriteRepo(IAsyncDbConnection connection)
{
    [LinqQuery(nameof(VersionQuery))]
    public partial Task<LinqItemVersion?> GetVersionAsync(Guid id, CancellationToken ct);

    [LinqCommand(nameof(PromoteQuery), ThrowOnNoRows = true)]
    public partial Task PromoteAsync(Guid id, uint expected, CancellationToken ct);

    [LinqCommand(nameof(DeleteByStatusQuery))]
    public partial Task<int> DeleteByStatusAsync(Status status, CancellationToken ct);

    [LinqQuery(nameof(ClaimQuery))]
    public partial Task<List<LinqItemVersion>> ClaimAsync(Status status, int count, IAsyncDbTransaction tx, CancellationToken ct);

    private static IQuery<LinqItemVersion> VersionQuery(Guid id) =>
        Sql.Table<LinqItem>().Where(i => i.Id == id).Select(i => new LinqItemVersion(i.Id, i.Rank, Sql.Xmin(i)));

    private static int PromoteQuery(Guid id, uint expected) =>
        Sql.Table<LinqItem>()
            .Where(i => i.Id == id && Sql.Xmin(i) == expected)
            .ExecuteUpdate(set => set.SetProperty(i => i.Rank, i => i.Rank + 1));

    private static int DeleteByStatusQuery(Status status) =>
        Sql.Table<LinqItem>().Where(i => i.Status == status).ExecuteDelete();

    // The lowest-ranked `count` rows of a status that no other transaction holds.
    private static IQuery<LinqItemVersion> ClaimQuery(Status status, int count) =>
        Sql.Table<LinqItem>()
            .Where(i => i.Status == status)
            .OrderBy(i => i.Rank)
            .Take(count)
            .ForUpdateSkipLocked()
            .Select(i => new LinqItemVersion(i.Id, i.Rank, Sql.Xmin(i)));
}
