using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// ZAO040 fires when the return-type element doesn't match ANY construction
// strategy on the ConventionDiscovery priority ladder — no [ValueObject], no
// static factory, no single-arg-ctor record, no multi-arg ctor, no enum, no
// primitive. Distinct from ZAO022 (info: shape-level "not yet supported") in
// that ZAO040 is the type-level "can't be constructed at all" error.
public class ZAO040Tests
{
    [Fact]
    public void Class_without_any_construction_strategy_emits_ZAO040()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            // Class with no public ctor with params, no factory, no [ValueObject].
            public sealed class Mystery {}

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1 FROM Mysteries LIMIT 1")]
                public partial Task<Mystery?> GetAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO040", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Record_with_resolvable_columns_does_not_emit_ZAO040()
    {
        // Positive control — a positional record with primitive columns has a clear
        // construction strategy (FlatRow). ZAO040 must not fire.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Total FROM Orders LIMIT 1")]
                public partial Task<OrderRow?> GetAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO040", System.StringComparison.Ordinal));
    }
}
