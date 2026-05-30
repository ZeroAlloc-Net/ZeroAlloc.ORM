using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// ZAO041 fires when a [Query] method parameter's type doesn't match ANY binding
// convention — no primitive, no enum, no [ValueObject] Value, no static From
// factory, no single-arg-ctor record. Mirrors ZAO040 but on the parameter (input)
// side instead of the return-type (output) side.
public class ZAO041Tests
{
    [Fact]
    public void Parameter_without_any_binding_strategy_emits_ZAO041()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            // Class with no Value property, no static factory, no [ValueObject].
            public sealed class Filter {}

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1 FROM Orders WHERE Filter = @filter")]
                public partial Task<int> CountAsync(Filter filter, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO041", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Primitive_parameter_does_not_emit_ZAO041()
    {
        // Positive control — int is a primitive with a clear binding strategy.
        // ZAO041 must not fire.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1 FROM Orders WHERE Id = @id")]
                public partial Task<int> CountAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO041", System.StringComparison.Ordinal));
    }
}
