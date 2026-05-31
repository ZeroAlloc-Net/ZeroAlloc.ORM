using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.3 Phase B.1 — detection snapshot for the MultiResultSet emit shape. Originally
// landed against a NotImplementedException stub before B.2-B.4 wired the real emit;
// the snapshot was refreshed when B.4 landed BatchWithFallback. Today this test
// effectively asserts the same Auto-batch emit shape as MultiResultSetAutoTests
// but is kept as a stable historic checkpoint anchoring the model's detection.
public class MultiResultSetDetectionTests
{
    [Fact]
    public Task Tuple_with_record_head_and_list_lines_classified_as_MultiResultSet()
    {
        var source =
            "using System.Collections.Generic;\n" +
            "using System.Data.Async;\n" +
            "using System.Threading;\n" +
            "using System.Threading.Tasks;\n" +
            "using ZeroAlloc.ORM;\n" +
            "\n" +
            "namespace TestApp;\n" +
            "\n" +
            "public sealed record OrderRow(int Id, int CustomerId, decimal Total);\n" +
            "public sealed record OrderLineRow(string Sku, int Quantity);\n" +
            "\n" +
            "public sealed partial class Repo(IAsyncDbConnection connection)\n" +
            "{\n" +
            "    [Query(\"SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Sku, Quantity FROM OrderLines WHERE OrderId = @id;\")]\n" +
            "    public partial Task<(OrderRow Head, List<OrderLineRow> Lines)?> GetWithLinesAsync(int id, CancellationToken ct);\n" +
            "}\n";
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
