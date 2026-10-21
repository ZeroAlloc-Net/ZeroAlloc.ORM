using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// A nullable enum bound as a parameter, a composite field or a bulk-insert row property must
// bind DBNull when null. `(int)x` on a null `Color?` throws, and `x.ToString()` on one gives
// the empty string, so the emit casts to `int?` and calls `?.ToString()` instead. A
// non-nullable enum keeps its existing emit.
public class NullableEnumParameterTests
{
    private const string Types = """
        #nullable enable
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public enum Color { Red, Blue }

        [StoreAsString]
        public enum Shape { Square, Round }

        """;

    [Fact]
    public void Nullable_enum_parameters_bind_null()
    {
        AssertEmits(
            Types + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO T (Color, Shape, Plain) VALUES (@color, @shape, @plain)")]
                public partial Task<int> InsertAsync(Color? color, Shape? shape, Color plain, CancellationToken ct);
            }
            """,
            "__p_color.Value = (object?)(int?)@color ?? global::System.DBNull.Value;",
            "__p_shape.Value = (object?)@shape?.ToString() ?? global::System.DBNull.Value;",
            "__p_plain.Value = (int)@plain;");
    }

    [Fact]
    public void Nullable_enum_query_parameters_bind_null()
    {
        AssertEmits(
            Types + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT count(*) FROM T WHERE (@color IS NULL OR Color = @color) AND (@shape IS NULL OR Shape = @shape)")]
                public partial Task<int> CountAsync(Color? color, Shape? shape, CancellationToken ct);
            }
            """,
            "__p_color.Value = (object?)(int?)@color ?? global::System.DBNull.Value;",
            "__p_shape.Value = (object?)@shape?.ToString() ?? global::System.DBNull.Value;");
    }

    [Fact]
    public void Nullable_enum_composite_fields_bind_null()
    {
        AssertEmits(
            Types + """
            public readonly record struct Paint(Color? Color, Shape? Shape);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO T (Color, Shape) VALUES (@paint_Color, @paint_Shape)")]
                public partial Task<int> InsertAsync(Paint paint, CancellationToken ct);

                [Command("INSERT INTO T (Color, Shape) VALUES (@paint_Color, @paint_Shape)")]
                public partial Task<int> InsertMaybeAsync(Paint? paint, CancellationToken ct);
            }
            """,
            "(object?)(int?)@paint.@Color ?? global::System.DBNull.Value;",
            "(object?)@paint.@Shape?.ToString() ?? global::System.DBNull.Value;",
            "(int?)@paint.Value.@Color",
            "@paint.Value.@Shape?.ToString()");
    }

    [Fact]
    public void Nullable_enum_bulk_insert_properties_bind_null()
    {
        AssertEmits(
            Types + """
            public sealed record Row(int Qty, Color? Color, Shape? Shape);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO T (Qty, Color, Shape) VALUES (@Qty, @Color, @Shape)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertAsync(IReadOnlyList<Row> rows, CancellationToken ct);
            }
            """,
            ".Value = (object?)(int?)__row.Color ?? global::System.DBNull.Value;",
            ".Value = (object?)__row.Shape?.ToString() ?? global::System.DBNull.Value;");
    }

    private static void AssertEmits(string source, params string[] expected)
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source);
        Assert.DoesNotContain(result.Results[0].Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var generated = string.Concat(result.GeneratedTrees.Select(t => t.ToString()));
        foreach (var line in expected)
            Assert.Contains(line, generated, StringComparison.Ordinal);

        var generatedPaths = result.GeneratedTrees.Select(t => t.FilePath).ToHashSet(StringComparer.Ordinal);
        var problems = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                || (d.Severity == DiagnosticSeverity.Warning
                    && d.Location.SourceTree is { } tree
                    && generatedPaths.Contains(tree.FilePath)))
            .Select(d => d.ToString())
            .ToList();
        Assert.Empty(problems);
    }
}
