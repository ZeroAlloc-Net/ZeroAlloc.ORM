using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// v0.5 Phase A.4 — composite materialization round-trip surface against Sqlite.
// Each method exercises a different shape:
//
//   * GetTotalAsync             -- scalar-position composite (Task<Money>).
//   * GetCompositeOrderRowAsync -- nested composite in FlatRow (Task<CompositeOrderRow?>).
//   * GetOrderEntityAsync       -- nested composite in DomainEntity, post-review
//                                  Fix 2 (Task<CompositeOrderEntity?>). Inner
//                                  reads go through GetOrdinal(name) so SELECT
//                                  column order is not load-bearing — the test
//                                  intentionally SELECTs columns in reverse
//                                  order to prove that.
//   * GetMoneyWithOrderIdAsync  -- composite with a value-object inner ctor
//                                  parameter (Task<MoneyWithOrderId>).
//
// `Task<List<CompositeOrderRow>>` is intentionally not present — `Task<List<T>>`
// is not a supported top-level shape in v0.5; IAsyncEnumerable<T> is the
// streaming surface (see StreamingRepo). Adding the list shape is a follow-up.
public sealed partial class CompositeRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
    public partial Task<Money> GetTotalAsync(int id, CancellationToken ct);

    [Query("SELECT Id, Amount, Currency FROM Orders WHERE Id = @id")]
    public partial Task<CompositeOrderRow?> GetCompositeOrderRowAsync(int id, CancellationToken ct);

    // SELECT column order deliberately differs from the ctor parameter order
    // (Currency, Amount, Id) to prove the DomainEntity GetOrdinal(name) lookup
    // is doing the work.
    [Query("SELECT Currency, Amount, Id FROM Orders WHERE Id = @id")]
    public partial Task<CompositeOrderEntity?> GetOrderEntityAsync(int id, CancellationToken ct);

    [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
    public partial Task<MoneyWithOrderId> GetMoneyWithOrderIdAsync(int id, CancellationToken ct);

    // v0.5 Phase B.3 — composite parameter binding round-trip surface.
    //
    //   * InsertOrderAsync         -- composite parameter unpacks into
    //                                 `@total_Amount` + `@total_Currency` SQL
    //                                 placeholders for an INSERT.
    //   * UpdateAmountAsync        -- composite parameter as a WHERE-clause
    //                                 predicate (both fields). Pins the SQL
    //                                 referencing the unpacked names by
    //                                 convention.
    //   * InsertMoneyWithOrderIdAsync -- composite parameter whose inner field
    //                                 is itself a ValueObject. Verifies the
    //                                 layered convention unwrap (`.Value`)
    //                                 happens at bind time, matching the
    //                                 Phase A read-side recursion.
    [Command("INSERT INTO Orders (Id, Amount, Currency) VALUES (@id, @total_Amount, @total_Currency)")]
    public partial Task<int> InsertOrderAsync(int id, Money total, CancellationToken ct);

    [Command("UPDATE Orders SET Amount = @newAmount WHERE Amount = @total_Amount AND Currency = @total_Currency")]
    public partial Task<int> UpdateAmountAsync(Money total, decimal newAmount, CancellationToken ct);

    [Command("INSERT INTO Orders (Id, Amount, Currency) VALUES (@id, @total_Amount, @total_Currency)")]
    public partial Task<int> InsertMoneyWithOrderIdAsync(int id, MoneyWithOrderId total, CancellationToken ct);
}
