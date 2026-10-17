using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// PostgreSQL arrays end to end: parameters bind as the CLR array, columns read through
// GetFieldValue of the array type. PostgresArrayTests runs every method on a full
// NpgsqlConnection and on a slim data source with EnableArrays.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class ArrayRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO tagged (id, tags) VALUES (@id, @tags)")]
    public partial Task<int> InsertAsync(Guid id, string[]? tags, CancellationToken ct);

    [Query("SELECT id, tags FROM tagged WHERE id = ANY(@ids) ORDER BY id")]
    public partial Task<IReadOnlyList<TaggedRow>> GetByIdsAsync(List<Guid> ids, CancellationToken ct);

    [Query("SELECT id, tags FROM tagged WHERE id = @id")]
    public partial Task<TaggedRow?> GetAsync(Guid id, CancellationToken ct);

    [Query("SELECT id, tags FROM tagged WHERE id = @id")]
    public partial Task<TaggedListRow?> GetListAsync(Guid id, CancellationToken ct);

    [Query("SELECT id, tags FROM tagged WHERE id = @id")]
    public partial Task<NullableTaggedRow?> GetNullableAsync(Guid id, CancellationToken ct);

    [Query("SELECT id, tags FROM tagged WHERE tags && @tags ORDER BY id")]
    public partial Task<IReadOnlyList<TaggedRow>> GetOverlappingAsync(IReadOnlyList<string> tags, CancellationToken ct);
}
