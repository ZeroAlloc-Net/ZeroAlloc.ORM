using System.Threading.Tasks;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.3 Phase B.2 — IAsyncDbBatch path snapshot. Uses [Query(Batch =
// BatchMode.Always)] to force the BatchAlways strategy so the snapshot exclusively
// covers the batch emit without the runtime fork from BatchWithFallback.
public class MultiResultSetBatchTests
{
    [Fact]
    public void Tuple_with_record_and_list_emits_IAsyncDbBatch_path()
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
            "    [Query(\"SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Sku, Quantity FROM OrderLines WHERE OrderId = @id;\", Batch = BatchMode.Always)]\n" +
            "    public partial Task<(OrderRow Head, List<OrderLineRow> Lines)?> GetWithLinesAsync(int id, CancellationToken ct);\n" +
            "}\n";
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    // #249 — a non-nullable scalar element throws on NULL naming the column and
    // the tuple element; the nullable one reads NULL as null.
    [Fact]
    public void Tuple_with_scalar_elements_guards_the_non_nullable_one()
    {
        var source =
            "using System.Data.Async;\n" +
            "using System.Threading;\n" +
            "using System.Threading.Tasks;\n" +
            "using ZeroAlloc.ORM;\n" +
            "\n" +
            "namespace TestApp;\n" +
            "\n" +
            "public sealed partial class Repo(IAsyncDbConnection connection)\n" +
            "{\n" +
            "    [Query(\"SELECT COUNT(*) FROM Orders; SELECT MAX(Total) FROM Orders;\", Batch = BatchMode.Always)]\n" +
            "    public partial Task<(int Count, decimal? MaxTotal)> GetStatsAsync(CancellationToken ct);\n" +
            "}\n";
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }
}
