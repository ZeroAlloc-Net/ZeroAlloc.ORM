using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// #328 - overloads of one method name, each with its own SQL.
public sealed partial class OverloadRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO TA (A) VALUES (@A)", Kind = CommandKind.BulkInsert)]
    public partial Task<int> InsertAsync(IReadOnlyList<OverloadRowA> rows, CancellationToken ct);

    [Command("INSERT INTO TB (Id) VALUES (@Id)", Kind = CommandKind.BulkInsert)]
    public partial Task<int> InsertAsync(IReadOnlyList<OverloadRowB> rows, CancellationToken ct);

    [Query("SELECT COUNT(*) FROM TA")]
    public partial Task<int> CountAsync(CancellationToken ct);

    [Query("SELECT COUNT(*) FROM TB WHERE Id >= @minId")]
    public partial Task<int> CountAsync(int minId, CancellationToken ct);
}
