using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// #464 — the table name comes from the repository instance at runtime, through {table}.
// TableLiteralRepo holds the same SQL with the table written out, for the allocation gate.
public sealed partial class TablePlaceholderRepo(IAsyncDbConnection connection, string table)
{
    [Command("INSERT INTO {table} (Id, Name) VALUES (@id, @name)")]
    public partial Task<int> InsertAsync(int id, string name, CancellationToken ct);

    [Query("SELECT Name FROM {table} WHERE Id = @id")]
    public partial Task<string?> GetNameAsync(int id, CancellationToken ct);

    [Query("SELECT COUNT(*) FROM {table}")]
    public partial Task<int> CountAsync(CancellationToken ct);

    [Query("SELECT COUNT(*) FROM {table} WHERE Id >= @minId")]
    public partial Task<int> CountAsync(int minId, CancellationToken ct);

    [Query("SELECT COUNT(*) FROM {table} WHERE Id = @id")]
    public partial ValueTask<int> CountByIdAsync(int id, CancellationToken ct);
}
