using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// Streaming detection test. Validates that ClassifyEmitShape routes IAsyncEnumerable<T>
// returns to EmitShape.Streaming. The snapshot captures the full emit produced by EmitStreaming.
public class StreamingDetectionTests
{
    [Fact]
    public Task IAsyncEnumerable_return_classified_as_Streaming()
    {
        var source =
            "using System.Collections.Generic;\n" +
            "using System.Data.Async;\n" +
            "using System.Runtime.CompilerServices;\n" +
            "using System.Threading;\n" +
            "using ZeroAlloc.ORM;\n" +
            "\n" +
            "namespace TestApp;\n" +
            "\n" +
            "public sealed record OrderRow(int Id, int CustomerId, decimal Total);\n" +
            "\n" +
            "public sealed partial class Repo(IAsyncDbConnection connection)\n" +
            "{\n" +
            "    [Query(\"SELECT Id, CustomerId, Total FROM Orders ORDER BY Id\")]\n" +
            "    public partial IAsyncEnumerable<OrderRow> StreamAsync([EnumeratorCancellation] CancellationToken ct);\n" +
            "}\n";
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
