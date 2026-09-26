using System.Data.Async;

namespace ZeroAlloc.ORM.Integration.Tests;

// v1.3 Task 9 — [Command(Kind = BulkInsert)] round-trip surface against Sqlite.
// Four method signatures cover the matrix:
//
//   * InsertOrdersAsync                 — INSERT chunked, returns rows-affected.
//   * InsertOrdersReturningIdsAsync     — INSERT ... RETURNING Id, returns identity list.
//   * InsertOrdersWithVoAsync           — TRow with [ValueObject] column, rows-affected.
//   * InsertOrdersReturningNullIdAsync  — #263: RETURNING NULL forces a NULL identity.
//
// Mirrors the connection-injection convention (primary-ctor field) used by every
// other integration-test repo in this assembly.
public sealed partial class BulkInsertRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)", Kind = CommandKind.BulkInsert)]
    public partial Task<int> InsertOrdersAsync(IReadOnlyList<BulkOrderRow> orders, CancellationToken ct);

    [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total) RETURNING Id", Kind = CommandKind.BulkInsert)]
    public partial Task<IReadOnlyList<int>> InsertOrdersReturningIdsAsync(IReadOnlyList<BulkOrderRow> orders, CancellationToken ct);

    [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)", Kind = CommandKind.BulkInsert)]
    public partial Task<int> InsertOrdersWithVoAsync(IReadOnlyList<BulkOrderRowWithVo> orders, CancellationToken ct);

    // #263 — no real identity column: RETURNING a NULL literal so the identity
    // readback meets DBNull for every row.
    [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total) RETURNING NULL", Kind = CommandKind.BulkInsert)]
    public partial Task<IReadOnlyList<int>> InsertOrdersReturningNullIdAsync(IReadOnlyList<BulkOrderRow> orders, CancellationToken ct);
}
