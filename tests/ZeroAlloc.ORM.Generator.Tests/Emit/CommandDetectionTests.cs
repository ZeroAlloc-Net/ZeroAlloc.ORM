using System.Linq;
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.4 Phase A.1 — [Command] attribute scanner pickup. Two coverage cells:
//
//   * Command_NonQuery_method_emits_partial_implementation — verifies that
//     a [Command("INSERT...")] method with Task<int> return reaches an emit
//     branch (snapshot proves the body shape, including the NonQuery
//     ExecuteNonQueryAsync call).
//
//   * Method_with_query_and_command_attributes_emits_ZAO005 — verifies that
//     the existing ZAO005 multi-attribute diagnostic extends to cover the
//     [Query] + [Command] overlap case introduced in v0.4.
public class CommandDetectionTests
{
    [Fact]
    public Task Command_NonQuery_method_emits_partial_implementation()
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
    public void Method_with_query_and_command_attributes_emits_ZAO005()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                [Command("INSERT INTO X VALUES (1)")]
                public partial Task<int> DoSomethingAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO005", System.StringComparison.Ordinal));
    }
}
