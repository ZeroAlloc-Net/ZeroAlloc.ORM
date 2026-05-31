using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.5 Phase A.2 — composite materialization at scalar position. The user
// declares `Task<Money>` where `Money(decimal, string)`; the generator emits a
// single ExecuteReaderAsync + Read + `new Money(GetDecimal(0), GetString(1))`
// construction. Snapshots pin the exact emit shape so regressions surface as
// diff churn.
public class CompositeEmitTests
{
    [Fact]
    public Task Composite_scalar_return_emits_inline_constructor()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<Money> GetTotalAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Composite_scalar_with_value_object_inner_unwraps_via_factory()
    {
        // The composite's second ctor parameter is itself a ValueObject (OrderId).
        // Materialization for OrderId is `OrderId.From(reader.GetInt32(N))` per
        // the existing ValueObject convention; nesting that inside the composite
        // construction exercises the layered convention behavior.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;
            using ZeroAlloc.ValueObjects;

            namespace TestApp;

            [ValueObject]
            public readonly partial struct OrderId
            {
                public int Value { get; }
                public OrderId(int v) { Value = v; }
                public static OrderId From(int value) => new(value);
            }

            public readonly record struct Money(decimal Amount, OrderId Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<Money> GetTotalAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
