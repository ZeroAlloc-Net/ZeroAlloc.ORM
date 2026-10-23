using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// #400 — a [Query] returning a non-nullable primitive scalar other than int, long, bool,
// double or decimal, or a list of a scalar, used to get no body and no error diagnostic.
// A non-nullable scalar reads column 0 of the first row and throws on no row and on NULL;
// a list of a scalar reads column 0 of every row.
public class QueryScalarShapeTests
{
    private const string Header = """
        using System;
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
        """;

    private static string Repo(string members) => Header + "\n" + members + "\n}\n";

    [Fact]
    public void Query_returning_Guid_reads_first_column()
    {
        var source = Repo("""
                [Query("SELECT id FROM t WHERE k = @k")]
                public partial Task<Guid> GetIdAsync(int k, CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Query_returning_valuetask_DateTimeOffset_reads_first_column()
    {
        var source = Repo("""
                [Query("SELECT at FROM t WHERE k = @k")]
                public partial ValueTask<DateTimeOffset> GetAtAsync(int k, CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Query_returning_string_reads_first_column()
    {
        var source = Repo("""
                [Query("SELECT name FROM t WHERE k = @k")]
                public partial Task<string> GetNameAsync(int k, CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Query_returning_List_of_string_reads_every_row()
    {
        var source = Repo("""
                [Query("SELECT name FROM t WHERE k > @k ORDER BY k")]
                public partial Task<List<string>> GetNamesAsync(int k, CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Query_returning_IReadOnlyList_of_Guid_reads_every_row()
    {
        var source = Repo("""
                [Query("SELECT id FROM t WHERE k > @k ORDER BY k")]
                public partial Task<IReadOnlyList<Guid>> GetIdsAsync(int k, CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Query_returning_IList_of_nullable_int_reads_every_row()
    {
        var source = Repo("""
                [Query("SELECT n FROM t WHERE k > @k ORDER BY k")]
                public partial Task<IList<int?>> GetNumbersAsync(int k, CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    // Every new shape compiles, with no generator diagnostic, no compile error and no
    // nullable warning in the emitted code, for each reader the catalog has.
    [Fact]
    public void Every_primitive_scalar_and_list_shape_compiles_cleanly()
    {
        var source = "#nullable enable\n" + Repo("""
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<Guid> GuidAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<DateTime> DateTimeAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial ValueTask<DateTimeOffset> DateTimeOffsetAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<TimeSpan> TimeSpanAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<DateOnly> DateOnlyAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<TimeOnly> TimeOnlyAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<short> ShortAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<float> FloatAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<byte> ByteAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<string> StringAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<byte[]> BytesAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t")] public partial Task<List<string>> StringListAsync(CancellationToken ct);
                [Query("SELECT v FROM t")] public partial Task<List<string?>> NullableStringListAsync(CancellationToken ct);
                [Query("SELECT v FROM t")] public partial ValueTask<IReadOnlyList<Guid>> GuidListAsync(CancellationToken ct);
                [Query("SELECT v FROM t")] public partial Task<IList<int?>> NullableIntListAsync(CancellationToken ct);
                [Query("SELECT v FROM t")] public partial Task<List<int>> IntListAsync(CancellationToken ct);
                [Query("SELECT v FROM t")] public partial Task<List<DateTimeOffset?>> NullableDtoListAsync(CancellationToken ct);
                [Query("SELECT v FROM t")] public partial Task<List<byte[]>> BytesListAsync(CancellationToken ct);
            """);
        var (run, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source);

        var generatorDiagnostics = run.Diagnostics
            .Select(d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}")
            .ToArray();
        Assert.True(generatorDiagnostics.Length == 0, string.Join("; ", generatorDiagnostics));

        var problems = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                || (d.Severity == DiagnosticSeverity.Warning && d.Id.StartsWith("CS86", System.StringComparison.Ordinal)))
            .Select(d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}")
            .ToArray();
        Assert.True(problems.Length == 0, string.Join("; ", problems));

        var generated = string.Concat(run.GeneratedTrees.Select(t => t.ToString()));
        Assert.DoesNotContain("TODO", generated, System.StringComparison.Ordinal);
    }

    // Oblivious `string`, outside a nullable context, reads as the non-nullable shape.
    [Fact]
    public void Oblivious_string_reads_as_non_nullable_scalar()
    {
        var source = "#nullable disable\n" + Repo("""
                [Query("SELECT name FROM t WHERE k = @k")]
                public partial Task<string> GetNameAsync(int k, CancellationToken ct);

                [Query("SELECT name FROM t")]
                public partial Task<List<string>> GetNamesAsync(CancellationToken ct);
            """);
        var (run, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(compileDiagnostics);
        var generated = string.Concat(run.GeneratedTrees.Select(t => t.ToString()));
        Assert.Contains("returned NULL, but its return type is the non-nullable 'string'", generated, System.StringComparison.Ordinal);
        Assert.Contains("is the non-nullable 'string'", generated, System.StringComparison.Ordinal);
    }

    // [Materialize(Factory)] keeps winning over the new non-nullable scalar shape.
    [Fact]
    public void Materialize_factory_still_wins_over_the_primitive_scalar_shape()
    {
        var source = Repo("""
                [Query("SELECT ticks FROM t WHERE k = @k")]
                [return: Materialize(Factory = "FromTicks")]
                public partial Task<TimeSpan> GetDurationAsync(int k, CancellationToken ct);
            """);
        var run = GeneratorHarness.RunGenerator(source);

        Assert.Empty(run.Results[0].Diagnostics);
        var generated = string.Concat(run.GeneratedTrees.Select(t => t.ToString()));
        Assert.Contains("global::System.TimeSpan.FromTicks(", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("returned no row, but its return type is the non-nullable", generated, System.StringComparison.Ordinal);
    }

    // A row type keeps winning over the list-of-scalar shape.
    [Fact]
    public void List_of_rows_still_reads_rows()
    {
        var source = Repo("""
                [Query("SELECT id, name FROM t")]
                public partial Task<List<Row>> GetRowsAsync(CancellationToken ct);
            """) + "public sealed record Row(Guid Id, string Name);\n";
        var run = GeneratorHarness.RunGenerator(source);

        Assert.Empty(run.Results[0].Diagnostics);
        var generated = string.Concat(run.GeneratedTrees.Select(t => t.ToString()));
        Assert.Contains("new global::TestApp.Row(", generated, System.StringComparison.Ordinal);
    }
}
