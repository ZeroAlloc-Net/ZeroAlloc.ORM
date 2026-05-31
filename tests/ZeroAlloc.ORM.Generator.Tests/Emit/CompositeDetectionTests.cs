using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.5 Phase A.1 — composite detection. The generator must recognise types whose
// public ctor has N positional parameters (each resolving to a primitive / VO /
// SingleArgCtor / StaticFactory) as a composite shape and expand its column count
// beyond the C# ctor arity. The classifier is exercised indirectly via the emit:
// the generated source carries a sentinel comment naming the composite type so
// the test can pin the classifier branch without depending on the full
// materialization emit landing in A.2/A.3.
public class CompositeDetectionTests
{
    [Fact]
    public void Scalar_composite_return_reaches_composite_branch()
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
        var generated = GeneratorHarness.RunGenerator(source)
            .GeneratedTrees
            .Single(t => t.FilePath.EndsWith("Repo.g.cs", System.StringComparison.Ordinal))
            .GetText()
            .ToString();

        Assert.Contains("// EmitShape: composite global::TestApp.Money (2 columns)", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_composite_in_flat_row_expands_column_count()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);
            public sealed record OrderRow(int Id, Money Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        var generated = GeneratorHarness.RunGenerator(source)
            .GeneratedTrees
            .Single(t => t.FilePath.EndsWith("Repo.g.cs", System.StringComparison.Ordinal))
            .GetText()
            .ToString();

        // Nested composite — the outer FlatRow has 2 ctor params but the flattened
        // column count is 3 (int Id + Money's two inner columns). The sentinel pins
        // the flattened count.
        Assert.Contains("// EmitShape: FlatRow with nested composite (flattened columns: 3)", generated, System.StringComparison.Ordinal);
    }
}
