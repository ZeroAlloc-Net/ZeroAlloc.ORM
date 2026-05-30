using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

public class ValueObjectMaterializationTests
{
    // [ValueObject] is provided by ZA.ValueObjects (force-loaded into the harness).
    // The wrapped Value property and static From factory are hand-rolled inline so the
    // snapshot doesn't depend on chaining a second source generator at test time —
    // ConventionDiscovery only needs to see the attribute + Value + From symbols, all
    // of which exist in this source.
    [Fact]
    public Task ValueObject_record_column_emits_From_factory_call()
    {
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

            public sealed record OrderRow(OrderId Id, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Total FROM Orders LIMIT 1")]
                public partial Task<OrderRow?> GetFirstAsync(CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
