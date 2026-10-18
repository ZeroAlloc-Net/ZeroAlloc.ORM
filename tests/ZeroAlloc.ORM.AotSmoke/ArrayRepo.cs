using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.AotSmoke;

// PostgreSQL array parameters and columns. Program.cs only runs these when ZAORM_AOT_PG names a
// server, but the generated code is always compiled and analysed by the AOT publish.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class ArrayRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO tagged (id, tags) VALUES (@id, @tags)")]
    public partial Task<int> InsertAsync(Guid id, string[] tags, CancellationToken ct);

    [Query("SELECT id, tags FROM tagged WHERE id = ANY(@ids) ORDER BY id")]
    public partial Task<IReadOnlyList<TaggedRow>> GetByIdsAsync(Guid[] ids, CancellationToken ct);

    [Query("SELECT id, tags FROM tagged WHERE id = @id")]
    public partial Task<TaggedListRow?> GetListAsync(Guid id, CancellationToken ct);
}
