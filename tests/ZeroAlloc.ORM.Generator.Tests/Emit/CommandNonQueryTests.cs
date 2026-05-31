using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.4 Phase A.2 — [Command(Kind = NonQuery)] emit shape. Two coverage cells:
//
//   * Task_int_return — INSERT statement with two parameters returning Task<int>;
//     emit invokes ExecuteNonQueryAsync and returns the rows-affected count.
//   * Task_no_result — DELETE statement returning Task (no value); emit awaits
//     ExecuteNonQueryAsync without a return statement.
//
// Both shapes share the open-on-execute / close-on-finally connection lifecycle,
// parameter binding via EmitParameterBinding, and the `await using var __cmd`
// pattern that v0.3 emit paths already use.
public class CommandNonQueryTests
{
    [Fact]
    public Task NonQuery_Task_int_emits_ExecuteNonQuery_with_return()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@cust, @total)")]
                public partial Task<int> InsertOrderAsync(int cust, decimal total, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task NonQuery_Task_void_emits_ExecuteNonQuery_without_return()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("DELETE FROM Orders WHERE Id = @id")]
                public partial Task DeleteOrderAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
