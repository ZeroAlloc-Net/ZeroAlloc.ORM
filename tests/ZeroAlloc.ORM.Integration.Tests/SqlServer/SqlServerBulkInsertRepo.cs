using System.Data.Async;

namespace ZeroAlloc.ORM.Integration.Tests.SqlServer;

// #263 — SQL Server round-trip for [Command(Kind = BulkInsert)] identity capture.
// SQL Server's `OUTPUT INSERTED.<col>` clause sits between the column list and
// VALUES, unlike Postgres/Sqlite's trailing `RETURNING`, so the two
// identity-returning methods below can't reuse BulkInsertRepo's SQL text and
// need their own repo. The two rows-affected cells (plain `INSERT ... VALUES`,
// no OUTPUT) don't have that problem — that SQL is provider-agnostic T-SQL —
// so SqlServerBulkInsertTests reuses the shared BulkInsertRepo for those,
// exactly as PostgresBulkInsertTests does.
public sealed partial class SqlServerBulkInsertRepo(IAsyncDbConnection connection)
{
    [Command(
        "INSERT INTO Orders (CustomerId, Total) OUTPUT INSERTED.Id VALUES (@CustomerId, @Total)",
        Kind = CommandKind.BulkInsert)]
    public partial Task<IReadOnlyList<int>> InsertOrdersReturningIdsAsync(IReadOnlyList<BulkOrderRow> orders, CancellationToken ct);

    // #263 — OUTPUT NULL forces a NULL identity for every row, to reproduce the
    // NULL guard against SqlClient's own NULL-read exception (SqlNullValueException).
    [Command(
        "INSERT INTO Orders (CustomerId, Total) OUTPUT NULL VALUES (@CustomerId, @Total)",
        Kind = CommandKind.BulkInsert)]
    public partial Task<IReadOnlyList<int>> InsertOrdersReturningNullIdAsync(IReadOnlyList<BulkOrderRow> orders, CancellationToken ct);
}
