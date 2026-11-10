using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// Ten placeholder methods on one repository: its instance must be no larger than a repository with
// none, apart from the one stored table string.
public sealed partial class TableTenMethodsRepo(IAsyncDbConnection connection, string table)
{
    [Query("SELECT COUNT(*) FROM {table} WHERE Id = @id AND 1 = 1")]
    public partial ValueTask<int> CountVariant1Async(int id, CancellationToken ct);

    [Query("SELECT COUNT(*) FROM {table} WHERE Id = @id AND 2 = 2")]
    public partial ValueTask<int> CountVariant2Async(int id, CancellationToken ct);

    [Query("SELECT COUNT(*) FROM {table} WHERE Id = @id AND 3 = 3")]
    public partial ValueTask<int> CountVariant3Async(int id, CancellationToken ct);

    [Query("SELECT COUNT(*) FROM {table} WHERE Id = @id AND 4 = 4")]
    public partial ValueTask<int> CountVariant4Async(int id, CancellationToken ct);

    [Query("SELECT COUNT(*) FROM {table} WHERE Id = @id AND 5 = 5")]
    public partial ValueTask<int> CountVariant5Async(int id, CancellationToken ct);

    [Query("SELECT COUNT(*) FROM {table} WHERE Id = @id AND 6 = 6")]
    public partial ValueTask<int> CountVariant6Async(int id, CancellationToken ct);

    [Query("SELECT COUNT(*) FROM {table} WHERE Id = @id AND 7 = 7")]
    public partial ValueTask<int> CountVariant7Async(int id, CancellationToken ct);

    [Query("SELECT COUNT(*) FROM {table} WHERE Id = @id AND 8 = 8")]
    public partial ValueTask<int> CountVariant8Async(int id, CancellationToken ct);

    [Query("SELECT COUNT(*) FROM {table} WHERE Id = @id AND 9 = 9")]
    public partial ValueTask<int> CountVariant9Async(int id, CancellationToken ct);

    [Query("SELECT COUNT(*) FROM {table} WHERE Id = @id AND 10 = 10")]
    public partial ValueTask<int> CountVariant10Async(int id, CancellationToken ct);
}
