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

    // Two statements, both on {table}. BatchMode.Auto runs the batch where the provider has one
    // and the joined text where it has not: Sqlite takes the joined path.
    [Query("SELECT Id, Name FROM {table} WHERE Id < @max ORDER BY Id; SELECT Id, Name FROM {table} WHERE Id >= @max ORDER BY Id;")]
    public partial Task<(IReadOnlyList<PlaceholderRow> Low, IReadOnlyList<PlaceholderRow> High)> SplitAsync(int max, CancellationToken ct);

    [Query(
        "SELECT Id, Name FROM {table} WHERE Id < @max ORDER BY Id; SELECT Id, Name FROM {table} WHERE Id >= @max ORDER BY Id;",
        Batch = BatchMode.Never)]
    public partial Task<(IReadOnlyList<PlaceholderRow> Low, IReadOnlyList<PlaceholderRow> High)> SplitJoinedAsync(int max, CancellationToken ct);

    [Query(
        "SELECT Id, Name FROM {table} WHERE Id < @max ORDER BY Id; SELECT Id, Name FROM {table} WHERE Id >= @max ORDER BY Id;",
        Batch = BatchMode.Always)]
    public partial Task<(IReadOnlyList<PlaceholderRow> Low, IReadOnlyList<PlaceholderRow> High)> SplitBatchAsync(int max, CancellationToken ct);

    [Command("INSERT INTO {table} (Id, Name) VALUES (@id, 'a'); INSERT INTO {table} (Id, Name) VALUES (@id + 1, 'b')")]
    public partial Task<int> InsertTwoAsync(int id, CancellationToken ct);
}
