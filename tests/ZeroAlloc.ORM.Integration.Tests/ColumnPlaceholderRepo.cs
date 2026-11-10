using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// #464 — {col:Name} quotes by the declared dialect when the QuoteColumns member is true. The
// dialect sits on this type, not on the assembly, so no other repository changes. Order is a
// reserved word: only the quoted form can name that column.
[Dialect(SqlDialect.Sqlite)]
public sealed partial class ColumnPlaceholderRepo(IAsyncDbConnection connection, string table, bool quoteColumns)
{
    [Command("INSERT INTO {table} ({col:Id}, {col:Order}) VALUES (@id, @value)")]
    public partial Task<int> InsertOrderAsync(int id, string value, CancellationToken ct);

    [Query("SELECT {col:Order} FROM {table} WHERE {col:Id} = @id")]
    public partial Task<string?> GetOrderAsync(int id, CancellationToken ct);

    [Command("INSERT INTO {table} ({col:Id}, {col:Name}) VALUES (@id, @value)")]
    public partial Task<int> InsertNameAsync(int id, string value, CancellationToken ct);

    [Query("SELECT {col:Name} FROM {table} WHERE {col:Id} = @id")]
    public partial Task<string?> GetNameAsync(int id, CancellationToken ct);
}
