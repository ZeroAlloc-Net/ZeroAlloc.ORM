using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// v0.4 Phase A.3 — round-trip integration coverage for [Command(Kind = NonQuery)]
// emit. Mirrors the connection-injection convention (primary-ctor field) used by
// the other integration-test repos. Four methods cover the matrix:
//
//   * InsertOrderAsync          — INSERT one row, returns rows-affected (1).
//   * UpdateOrdersByCustomerAsync — UPDATE matching multiple rows, returns affected count.
//   * DeleteOrderByIdAsync      — DELETE WHERE Id = @id with no match, returns 0.
//   * TouchOrderAsync           — UPDATE returning Task (no value), exercises the
//                                 arity-0 emit branch.
public sealed partial class CommandRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO Orders (Id, CustomerId, Total) VALUES (@id, @cust, @total)")]
    public partial Task<int> InsertOrderAsync(int id, int cust, decimal total, CancellationToken ct);

    [Command("UPDATE Orders SET Total = @newTotal WHERE CustomerId = @cust")]
    public partial Task<int> UpdateOrdersByCustomerAsync(int cust, decimal newTotal, CancellationToken ct);

    [Command("DELETE FROM Orders WHERE Id = @id")]
    public partial Task<int> DeleteOrderByIdAsync(int id, CancellationToken ct);

    [Command("UPDATE Orders SET Total = Total + 1 WHERE Id = @id")]
    public partial Task TouchOrderAsync(int id, CancellationToken ct);

    // v0.4 Phase B.2 — [Command(Kind = Scalar)] round-trip coverage. Four methods
    // mirror the snapshot matrix: COUNT(*) -> int, SUM(Total) WHERE Customer ->
    // decimal, MAX(Created) on empty -> DateTime?, SUM(Total) -> value-object.
    [Command("SELECT COUNT(*) FROM Orders", Kind = CommandKind.Scalar)]
    public partial Task<int> CountOrdersAsync(CancellationToken ct);

    [Command("SELECT COALESCE(SUM(Total), 0) FROM Orders WHERE CustomerId = @cust", Kind = CommandKind.Scalar)]
    public partial Task<decimal> SumTotalsForCustomerAsync(int cust, CancellationToken ct);

    [Command("SELECT MAX(Created) FROM Orders", Kind = CommandKind.Scalar)]
    public partial Task<DateTime?> MaxCreatedAsync(CancellationToken ct);

    [Command("SELECT COALESCE(SUM(Total), 0) FROM Orders WHERE CustomerId = @cust", Kind = CommandKind.Scalar)]
    public partial Task<TotalAmount> SumTotalsValueObjectAsync(int cust, CancellationToken ct);

    // v0.4 Phase B code-review Fix 1 regression coverage. A non-nullable
    // `Task<int>` scalar against a SELECT that produces NO ROWS yields a null
    // `__result` from ExecuteScalarAsync. The generator's null-guard must throw
    // InvalidOperationException instead of letting Convert.ToInt32(null, ic)
    // silently return 0 — a data-corruption hazard for callers expecting an
    // actual scalar.
    [Command("SELECT Total FROM Orders WHERE Id = -999", Kind = CommandKind.Scalar)]
    public partial Task<decimal> GetTotalForMissingIdAsync(CancellationToken ct);
}
