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
}
