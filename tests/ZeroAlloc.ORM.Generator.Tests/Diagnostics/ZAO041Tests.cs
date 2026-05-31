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

    [Fact]
    public void Nullable_reference_type_composite_parameter_emits_ZAO041()
    {
        // v0.5 Phase C post-review Fix 1 — nullable REFERENCE-type composite
        // parameters (`OrderRow?` where OrderRow is a class) are gated out of
        // the nullable-composite bind branch because the `.Value` accessor
        // emit relies on `Nullable<T>` and would produce CS1061 for a class.
        // The classifier leaves `CompositeFields` empty so the ZAO041
        // "no binding strategy" sentinel fires — adopters see a build-time
        // failure pointing at the unsupported parameter shape instead of the
        // confusing CS1061 at consumer compile time.
        //
        // The non-nullable class composite (`OrderRow row` without `?`)
        // continues to bind via the standard MultiArgCtor unpacking; only
        // the nullable variant falls through.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            // Class composite (not a record / record struct) — `.Value` doesn't
            // exist on `OrderRow?`, so the nullable bind branch is unsafe.
            public sealed class OrderRow
            {
                public int Id { get; }
                public decimal Amount { get; }
                public OrderRow(int id, decimal amount) { Id = id; Amount = amount; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET Amount = @row_Amount WHERE Id = @row_Id", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateAsync(OrderRow? row, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO041", System.StringComparison.Ordinal));
        // ZAO050 must NOT fire — reference-type nullable composite isn't in the
        // Phase C contract today (only `Nullable<T>` struct composites trigger it).
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO050", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Non_nullable_reference_type_composite_parameter_does_not_emit_ZAO041()
    {
        // Positive control — a non-nullable class composite parameter binds
        // via the standard MultiArgCtor positional unpacking. ZAO041 / ZAO050
        // must not fire.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed class OrderRow
            {
                public int Id { get; }
                public decimal Amount { get; }
                public OrderRow(int id, decimal amount) { Id = id; Amount = amount; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET Amount = @row_Amount WHERE Id = @row_Id", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateAsync(OrderRow row, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO041", System.StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO050", System.StringComparison.Ordinal));
    }
}
