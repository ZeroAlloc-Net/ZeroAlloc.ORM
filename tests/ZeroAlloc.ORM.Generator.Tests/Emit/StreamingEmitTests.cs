using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.3 Phase C.2 — yield-based async iterator emit for IAsyncEnumerable<T>.
// Verifies the full body shape: open-on-execute lifecycle, per-row
// `yield return new TElement(...)` materialization, parameter binding
// before the reader loop, and try/finally close-on-finally cleanup.
public class StreamingEmitTests
{
    [Fact]
    public Task IAsyncEnumerable_emits_yield_based_async_iterator()
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
            "    [Query(\"SELECT Id, CustomerId, Total FROM Orders WHERE CustomerId = @customerId ORDER BY Id\")]\n" +
            "    public partial IAsyncEnumerable<OrderRow> StreamByCustomerAsync(int customerId, [EnumeratorCancellation] CancellationToken ct);\n" +
            "}\n";
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
