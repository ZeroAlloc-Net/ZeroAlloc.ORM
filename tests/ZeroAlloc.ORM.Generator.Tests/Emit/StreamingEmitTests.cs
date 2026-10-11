using System.Threading.Tasks;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.3 Phase C.2 — yield-based async iterator emit for IAsyncEnumerable<T>.
// Verifies the full body shape: open-on-execute lifecycle, per-row
// `yield return new TElement(...)` materialization, parameter binding
// before the reader loop, and try/finally close-on-finally cleanup.
public class StreamingEmitTests
{
    [Fact]
    public void IAsyncEnumerable_emits_yield_based_async_iterator()
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
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void IAsyncEnumerable_of_DomainEntity_resolves_ordinals_before_the_read_loop()
    {
        // #326 - GetOrdinal runs once per stream, before the first ReadAsync,
        // not once per row. The nullable column reads through the same local.
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed class Customer
            {
                public Customer(int id, string name, string? nickname) { Id = id; Name = name; Nickname = nickname; }
                public int Id { get; }
                public string Name { get; }
                public string? Nickname { get; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Name, Nickname FROM Customers ORDER BY Id")]
                public partial IAsyncEnumerable<Customer> StreamAsync([EnumeratorCancellation] CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }
}
