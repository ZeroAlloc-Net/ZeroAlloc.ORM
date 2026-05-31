using System.Linq;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.5 Phase B.1 — composite parameter binding detection.
//
// A method parameter whose type resolves to ConventionKind.MultiArgCtor must
// be classified as a COMPOSITE binding: the generator emits N DbParameter
// blocks, one per ctor argument, named `@{paramName}_{ctorArgName}`. The
// classifier branch is pinned via a sentinel comment in the generated source
// so this test stays decoupled from the full unpacking emit (covered by
// CompositeBindingTests.cs).
//
// ZAO041 (no binding strategy) MUST NOT fire for composite-typed parameters —
// MultiArgCtor is a known convention and must reach the new binding branch.
public class CompositeBindingDetectionTests
{
    [Fact]
    public void Composite_parameter_marked_for_unpacking()
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
                [Command(Kind = CommandKind.NonQuery, Sql = "UPDATE Orders SET Amount = @total_Amount, Currency = @total_Currency WHERE Id = 1")]
                public partial Task<int> UpdateTotalAsync(Money total, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var generated = result.GeneratedTrees
            .Single(t => t.FilePath.EndsWith("Repo.g.cs", System.StringComparison.Ordinal))
            .GetText()
            .ToString();

        Assert.Contains(
            "// CompositeBinding: total -> global::TestApp.Money (fields: 2)",
            generated,
            System.StringComparison.Ordinal);

        // ZAO041 must NOT fire for a composite parameter — MultiArgCtor is a
        // known binding strategy now.
        var diagnostics = result.Results[0].Diagnostics;
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO041", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Composite_parameter_alongside_primitive()
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
                [Command(Kind = CommandKind.NonQuery, Sql = "UPDATE Orders SET Amount = @total_Amount, Currency = @total_Currency WHERE Id = @id")]
                public partial Task<int> UpdateAsync(int id, Money total, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var generated = result.GeneratedTrees
            .Single(t => t.FilePath.EndsWith("Repo.g.cs", System.StringComparison.Ordinal))
            .GetText()
            .ToString();

        // Composite sentinel for `total`.
        Assert.Contains(
            "// CompositeBinding: total -> global::TestApp.Money (fields: 2)",
            generated,
            System.StringComparison.Ordinal);

        // The primitive `@id` parameter still binds via the v0.1 path — its
        // parameter local must still be present.
        Assert.Contains("__p_id", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Composite_parameter_with_value_object_inner_field()
    {
        // Detection symmetry with Phase A's composite-with-VO read side. The
        // inner OrderId field has its own Convention attached so B.2's emit
        // can unwrap via `.Value` at bind time. This test pins the detection
        // and ZAO041 silence; the emit shape is locked in CompositeBindingTests.
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

            public readonly record struct MoneyWithOrderId(decimal Amount, OrderId Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command(Kind = CommandKind.NonQuery, Sql = "UPDATE Orders SET Amount = @outer_Amount, Currency = @outer_Currency WHERE Id = 1")]
                public partial Task<int> UpdateAsync(MoneyWithOrderId outer, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var generated = result.GeneratedTrees
            .Single(t => t.FilePath.EndsWith("Repo.g.cs", System.StringComparison.Ordinal))
            .GetText()
            .ToString();

        Assert.Contains(
            "// CompositeBinding: outer -> global::TestApp.MoneyWithOrderId (fields: 2)",
            generated,
            System.StringComparison.Ordinal);
        var diagnostics = result.Results[0].Diagnostics;
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO041", System.StringComparison.Ordinal));
    }
}
