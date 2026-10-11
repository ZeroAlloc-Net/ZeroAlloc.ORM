using System.Data.Async;

namespace ZeroAlloc.ORM.Integration.Tests.BulkInsertSql;

// #326 — the BulkInsert BulkInsertAllocationTests measures against Sqlite.
public sealed partial class LabelBulkInsertRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO Labels (Code, Label) VALUES (@Code, @Label)", Kind = CommandKind.BulkInsert)]
    public partial Task<int> InsertAsync(IReadOnlyList<LabelRow> rows, CancellationToken ct);
}
