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

    [Command("INSERT INTO nums (id, ints, longs) VALUES (@id, @ints, @longs)")]
    public partial Task<int> InsertNumsAsync(int id, int[] ints, long[] longs, CancellationToken ct);

    [Query("SELECT id, ints, longs FROM nums WHERE id = @id")]
    public partial Task<NumsRow?> GetNumsAsync(int id, CancellationToken ct);

    [Command("INSERT INTO reals (id, floats, doubles) VALUES (@id, @floats, @doubles)")]
    public partial Task<int> InsertRealsAsync(int id, float[] floats, double[] doubles, CancellationToken ct);

    [Query("SELECT id, floats, doubles FROM reals WHERE id = @id")]
    public partial Task<RealsRow?> GetRealsAsync(int id, CancellationToken ct);

    [Query("SELECT tags FROM tagged WHERE id = @id")]
    public partial Task<string[]> GetTagsAsync(Guid id, CancellationToken ct);

    [Command("SELECT tags FROM tagged WHERE id = @id", Kind = CommandKind.Scalar)]
    public partial Task<string[]> GetTagsScalarAsync(Guid id, CancellationToken ct);

    [Command("INSERT INTO posts (id, name, tags) VALUES (@Id, @Name, @Tags)", Kind = CommandKind.BulkInsert)]
    public partial Task<int> InsertPostsAsync(IReadOnlyList<PostRow> posts, CancellationToken ct);

    [Query("SELECT count(*)::int FROM posts WHERE name = @filter_Name AND tags && @filter_Tags")]
    public partial Task<int> CountPostsAsync(TagFilter filter, CancellationToken ct);
}
