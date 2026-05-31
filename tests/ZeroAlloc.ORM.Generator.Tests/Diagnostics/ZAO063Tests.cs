using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// v0.5 Phase B — ZAO063 fires when `[Param(Name = "...")]` is applied to a
// composite (MultiArgCtor) parameter. Composite binding emits N DbParameters
// positionally (`@{paramName}_{ctorArgName}`), so a single-name override
// cannot compose with the unpacking. Reporting at compile time prevents
// adopters from shipping a misleading attribute and discovering the no-op
// at runtime.
public class ZAO063Tests
{
    [Fact]
    public void Composite_parameter_with_Param_Name_override_emits_ZAO063()
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
                [Command("UPDATE Orders SET Amount = @custom_Amount, Currency = @custom_Currency WHERE Id = 1", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateTotalAsync([Param(Name = "@custom")] Money total, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO063", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Composite_parameter_without_Param_Name_does_not_emit_ZAO063()
    {
        // Negative control — the canonical composite-binding shape (no
        // Name override) must NOT fire ZAO063.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET Amount = @total_Amount, Currency = @total_Currency WHERE Id = 1", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateTotalAsync(Money total, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO063", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Primitive_parameter_with_Param_Name_override_does_not_emit_ZAO063()
    {
        // Negative control — `[Param(Name = ...)]` on a primitive parameter
        // is the supported use case (single DbParameter, single rename).
        // ZAO063 must NOT fire for non-composite types.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1 FROM Orders WHERE Id = @custom")]
                public partial Task<int> CountAsync([Param(Name = "@custom")] int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO063", System.StringComparison.Ordinal));
    }
}
