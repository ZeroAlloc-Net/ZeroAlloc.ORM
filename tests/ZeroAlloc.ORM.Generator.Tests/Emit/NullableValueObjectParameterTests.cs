using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// A nullable value object bound as a parameter, a composite field or a bulk-insert
// row property must bind its inner value, or DBNull when null. Emitting a plain
// `.Value` is wrong twice over: on `Sku?` it is Nullable<T>.Value, which yields the
// Sku struct itself and throws on null, and on `SkuRef?` it dereferences null.
// Each test snapshots the emit, then compiles it and checks that the expression
// coalesced to DBNull has the inner primitive type and draws no nullable warning.
public class NullableValueObjectParameterTests
{
    private const string Types = """
        #nullable enable
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public readonly record struct Sku(int Value);
        public sealed record SkuRef(int Value);

        """;

    [Fact]
    public void Nullable_struct_value_object_parameter_binds_inner_value()
    {
        var source = Types + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO T (Sku) VALUES (@sku)")]
                public partial Task<int> InsertAsync(Sku? sku, CancellationToken ct);
            }
            """;
        AssertBindsInnerValue(source, "__p_sku", "int?");
    }

    [Fact]
    public void Nullable_class_value_object_parameter_binds_inner_value()
    {
        var source = Types + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO T (Sku) VALUES (@sku)")]
                public partial Task<int> InsertAsync(SkuRef? sku, CancellationToken ct);
            }
            """;
        AssertBindsInnerValue(source, "__p_sku", "int?");
    }

    [Fact]
    public void Nullable_value_object_parameter_on_query_binds_inner_value()
    {
        var source = Types + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT COUNT(*) FROM T WHERE Sku IS @sku")]
                public partial Task<int> CountAsync(Sku? sku, CancellationToken ct);
            }
            """;
        AssertBindsInnerValue(source, "__p_sku", "int?");
    }

    [Fact]
    public void Nullable_value_object_composite_field_binds_inner_value()
    {
        var source = Types + """
            public readonly record struct Line(int Qty, Sku? Sku);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO T (Qty, Sku) VALUES (@line_Qty, @line_Sku)")]
                public partial Task<int> InsertAsync(Line line, CancellationToken ct);
            }
            """;
        AssertBindsInnerValue(source, "__p_line_Sku", "int?");
    }

    [Fact]
    public void Nullable_value_object_field_of_nullable_composite_binds_inner_value()
    {
        var source = Types + """
            public readonly record struct Line(int Qty, Sku? Sku);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO T (Qty, Sku) VALUES (@line_Qty, @line_Sku)")]
                public partial Task<int> InsertAsync(Line? line, CancellationToken ct);
            }
            """;
        AssertBindsInnerValue(source, "__p_line_Sku", "int?");
    }

    [Fact]
    public void Nullable_value_object_bulk_insert_property_binds_inner_value()
    {
        var source = Types + """
            public sealed record Row(int Qty, Sku? Sku);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO T (Qty, Sku) VALUES (@Qty, @Sku)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertAsync(IReadOnlyList<Row> rows, CancellationToken ct);
            }
            """;
        AssertBindsInnerValue(source, "__p_Sku_1", "int?");
    }

    // Finds every `<paramLocal>.Value = (object?)<expr> ?? DBNull.Value;` in the emitted
    // code and checks that <expr> has the inner primitive type, then that the emitted
    // code compiles without errors or warnings.
    private static void AssertBindsInnerValue(string source, string paramLocal, string expectedType)
    {
        var (runResult, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source);
        GeneratorSnapshot.Verify(runResult);

        var generated = compilation.SyntaxTrees
            .Where(t => t.FilePath.EndsWith(".g.cs", System.StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(generated);

        var boundTypes = generated
            .SelectMany(tree =>
            {
                var model = compilation.GetSemanticModel(tree);
                return tree.GetRoot().DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
                    .Where(a => string.Equals(a.Left.ToString(), paramLocal + ".Value", System.StringComparison.Ordinal))
                    .Select(a => a.Right)
                    .OfType<BinaryExpressionSyntax>()
                    .Where(b => b.IsKind(SyntaxKind.CoalesceExpression))
                    .Select(b => b.Left)
                    .OfType<CastExpressionSyntax>()
                    .Select(c => model.GetTypeInfo(c.Expression).Type?.ToDisplayString())
                    .ToArray();
            })
            .ToArray();
        Assert.NotEmpty(boundTypes);
        Assert.All(boundTypes, t => Assert.Equal(expectedType, t));

        var generatedDiagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Where(d => d.Location.SourceTree is { } tree && generated.Contains(tree))
            .Select(d => d.ToString())
            .ToArray();
        Assert.Empty(generatedDiagnostics);
    }
}
