using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// #250 — a scalar command whose single value is a database NULL. `SELECT NULL`
// runs unchanged on Sqlite, SQL Server and Postgres, and each provider hands the
// NULL back from ExecuteScalar as DBNull.Value. A non-nullable target throws
// ZeroAllocOrmMaterializationException; a nullable one receives null.
public sealed partial class ScalarNullRepo(IAsyncDbConnection connection)
{
    [Command("SELECT NULL", Kind = CommandKind.Scalar)]
    public partial Task<string> NullStringAsync(CancellationToken ct);

    [Command("SELECT NULL", Kind = CommandKind.Scalar)]
    public partial Task<int> NullIntAsync(CancellationToken ct);

    [Command("SELECT NULL", Kind = CommandKind.Scalar)]
    public partial Task<TotalAmount> NullValueObjectAsync(CancellationToken ct);

    [Command("SELECT NULL", Kind = CommandKind.Scalar)]
    public partial Task<string?> NullableStringAsync(CancellationToken ct);

    [Command("SELECT NULL", Kind = CommandKind.Scalar)]
    public partial Task<int?> NullableIntAsync(CancellationToken ct);
}
