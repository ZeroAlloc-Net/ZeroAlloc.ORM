using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.5 Phase A.1 — composite detection. The generator must recognise types whose
// public ctor has N positional parameters (each resolving to a primitive / VO /
// SingleArgCtor / StaticFactory) as a composite shape and expand its column count
// beyond the C# ctor arity. The classifier is exercised indirectly via the emit:
// the generated source carries a sentinel comment naming the composite type so
// the test pins the classifier branch with a small, focused assertion. A.2/A.3
// landed together with A.1 in the same PR; the broader CompositeEmitTests and
// CompositeNestedTests cover the full materialization snapshots in this repo.
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

        Assert.Contains("// EmitShape: composite global::TestApp.Money (flattened columns: 2)", generated, System.StringComparison.Ordinal);
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

    // Note: a third test here previously asserted that
    // `Task<List<OrderRow>>` where OrderRow embeds a non-nullable Money
    // composite emitted ZAO022. That assertion was retired in v1.6:
    // EmitListResultSet now recurses into composite InnerColumns, so the
    // non-nullable case materializes correctly. The nullable-composite
    // path is still deferred to ZAO022 and is pinned by
    // ListResultSetTests.ListResultSet_with_Nullable_Composite_still_rejected.
}
