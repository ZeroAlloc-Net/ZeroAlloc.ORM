using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// #250 — a scalar command whose single value is a database NULL. `SELECT NULL`
// runs unchanged on Sqlite, SQL Server and Postgres, and each provider hands the
// NULL back from ExecuteScalar as DBNull.Value. A non-nullable target throws
// ZeroAllocOrmMaterializationException; a nullable one receives null. A query
// that returns no row is held to the same contract (#260).
public sealed partial class ScalarNullRepo(IAsyncDbConnection connection)
{
    [Command("SELECT NULL", Kind = CommandKind.Scalar)]
    public partial Task<string> NullStringAsync(CancellationToken ct);

    [Command("SELECT NULL", Kind = CommandKind.Scalar)]
    public partial Task<int> NullIntAsync(CancellationToken ct);

    [Command("SELECT NULL", Kind = CommandKind.Scalar)]
    public partial Task<TotalAmount> NullValueObjectAsync(CancellationToken ct);

    [Command("SELECT NULL", Kind = CommandKind.Scalar)]
    public partial Task<Status> NullEnumAsync(CancellationToken ct);

    // An Identity command shares the scalar emit, with its own message: it has
    // no nullable variant to point at.
    [Command("SELECT NULL", Kind = CommandKind.Identity)]
    public partial Task<int> NullIdentityAsync(CancellationToken ct);

    [Command("SELECT NULL", Kind = CommandKind.Scalar)]
    public partial Task<string?> NullableStringAsync(CancellationToken ct);

    [Command("SELECT NULL", Kind = CommandKind.Scalar)]
    public partial Task<int?> NullableIntAsync(CancellationToken ct);

    // #260 — a scalar command whose query returns no row at all. ExecuteScalar
    // hands back null rather than DBNull. `SELECT 1 WHERE 1 = 0` runs unchanged on
    // all three providers.
    [Command("SELECT 1 WHERE 1 = 0", Kind = CommandKind.Scalar)]
    public partial Task<int> NoRowIntAsync(CancellationToken ct);

    [Command("SELECT 1 WHERE 1 = 0", Kind = CommandKind.Scalar)]
    public partial Task<TotalAmount> NoRowValueObjectAsync(CancellationToken ct);

    [Command("SELECT 1 WHERE 1 = 0", Kind = CommandKind.Identity)]
    public partial Task<int> NoRowIdentityAsync(CancellationToken ct);

    [Command("SELECT 1 WHERE 1 = 0", Kind = CommandKind.Scalar)]
    public partial Task<int?> NoRowNullableIntAsync(CancellationToken ct);
}
