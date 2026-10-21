using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// Nullable enums through [Command] and [Query] parameters: an int enum and a [StoreAsString] one.
// A null must bind DBNull. The casts type the parameters for PostgreSQL, since a [Query] sends
// them without a DbType.
public sealed partial class PostgresNullableEnumRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO nullable_enums (id, status, shape) VALUES (@id, @status, @shape)")]
    public partial Task<int> InsertAsync(int id, Status? status, StringStatus? shape, CancellationToken ct);

    [Query("""
        SELECT id, status, shape FROM nullable_enums
        WHERE (@status::int IS NULL OR status = @status::int) AND (@shape::text IS NULL OR shape = @shape::text)
        ORDER BY id
        """)]
    public partial Task<List<PostgresNullableEnumRow>> FindAsync(Status? status, StringStatus? shape, CancellationToken ct);
}
