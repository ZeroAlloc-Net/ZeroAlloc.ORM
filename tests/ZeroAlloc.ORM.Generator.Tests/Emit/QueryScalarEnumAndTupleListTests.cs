using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// #410 — an enum at [Query] scalar position, Task<Status>, Task<Status?> and
// List<Status>, and a list of a scalar as a tuple element, (int, List<string>), used
// to report ZAO022. An enum reads through its convention: the underlying integral
// reader and a cast, or GetString and Enum.Parse for [StoreAsString]. A tuple scalar
// list reads column 0 of every row of its result set.
public class QueryScalarEnumAndTupleListTests
{
    private const string Header = """
        using System;
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public enum Status { Open, Closed }

        public enum Tiny : byte { A, B }

        [StoreAsString]
        public enum Label { Red, Green }

        public sealed record Row(Guid Id, string Name);

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
        """;

    private static string Repo(string members) => Header + "\n" + members + "\n}\n";

    [Fact]
    public void Query_returning_enum_reads_first_column_with_a_cast()
    {
        var source = Repo("""
                [Query("SELECT status FROM t WHERE k = @k")]
                public partial Task<Status> GetStatusAsync(int k, CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Query_returning_nullable_enum_maps_no_row_and_NULL_to_null()
    {
        var source = Repo("""
                [Query("SELECT status FROM t WHERE k = @k")]
                public partial Task<Status?> GetStatusAsync(int k, CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Query_returning_List_of_string_enum_parses_every_row()
    {
        var source = Repo("""
                [Query("SELECT label FROM t ORDER BY k")]
                public partial Task<List<Label>> GetLabelsAsync(CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Tuple_with_scalar_list_elements_reads_column_zero_of_every_row()
    {
        var source = Repo("""
                [Query("SELECT COUNT(*) FROM t; SELECT name FROM t; SELECT n FROM t", Batch = BatchMode.Always)]
                public partial Task<(int Count, List<string> Names, IReadOnlyList<int?> Numbers)> GetSummaryAsync(CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    // Every new shape compiles, with no generator diagnostic, no compile error and no
    // nullable warning in the emitted code.
    [Fact]
    public void Every_enum_scalar_and_tuple_list_shape_compiles_cleanly()
    {
        var source = "#nullable enable\n" + Repo("""
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<Status> StatusAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial ValueTask<Status> StatusValueTaskAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<Status?> MaybeStatusAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial ValueTask<Tiny?> MaybeTinyAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<Tiny> TinyAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<Label> LabelAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<Label?> MaybeLabelAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t")] public partial Task<List<Status>> StatusListAsync(CancellationToken ct);
                [Query("SELECT v FROM t")] public partial Task<IReadOnlyList<Status?>> MaybeStatusListAsync(CancellationToken ct);
                [Query("SELECT v FROM t")] public partial Task<IList<Label>> LabelListAsync(CancellationToken ct);
                [Query("SELECT v FROM t; SELECT v FROM t")] public partial Task<(int Count, List<string> Names)> NamesAsync(CancellationToken ct);
                [Query("SELECT v FROM t; SELECT v FROM t")] public partial Task<(int Count, List<string?> Names)> MaybeNamesAsync(CancellationToken ct);
                [Query("SELECT v FROM t; SELECT v FROM t")] public partial Task<(int Count, List<int?> Numbers)> MaybeNumbersAsync(CancellationToken ct);
                [Query("SELECT v FROM t; SELECT v FROM t")] public partial Task<(Status Head, IReadOnlyList<Status> Statuses)> StatusesAsync(CancellationToken ct);
                [Query("SELECT v FROM t; SELECT v FROM t")] public partial Task<(Row? Head, IEnumerable<Label?> Labels)?> LabelsAsync(CancellationToken ct);
                [Query("SELECT v FROM t; SELECT v FROM t; SELECT v FROM t")] public partial Task<(List<Guid> Ids, List<Row> Rows, ICollection<DateTimeOffset> At)> MixedAsync(CancellationToken ct);
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
        Assert.DoesNotContain("new global::System.Guid(", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("new string(", generated, System.StringComparison.Ordinal);
    }

    // A byte-backed enum reads through its underlying reader.
    [Fact]
    public void Byte_backed_enum_reads_through_GetByte()
    {
        var source = Repo("""
                [Query("SELECT v FROM t WHERE k = @k")]
                public partial Task<Tiny> TinyAsync(int k, CancellationToken ct);
            """);
        var run = GeneratorHarness.RunGenerator(source);

        Assert.Empty(run.Results[0].Diagnostics);
        var generated = string.Concat(run.GeneratedTrees.Select(t => t.ToString()));
        Assert.Contains("return (global::TestApp.Tiny)__reader.GetByte(0);", generated, System.StringComparison.Ordinal);
    }

    // A tuple element that is a list of rows keeps reading rows.
    [Fact]
    public void Tuple_list_of_rows_still_reads_rows()
    {
        var source = Repo("""
                [Query("SELECT COUNT(*) FROM t; SELECT id, name FROM t")]
                public partial Task<(int Count, List<Row> Rows)> GetRowsAsync(CancellationToken ct);
            """);
        var run = GeneratorHarness.RunGenerator(source);

        Assert.Empty(run.Results[0].Diagnostics);
        var generated = string.Concat(run.GeneratedTrees.Select(t => t.ToString()));
        Assert.Contains("__elem1.Add(new global::TestApp.Row(", generated, System.StringComparison.Ordinal);
    }

    // A stored procedure tuple with an output parameter takes a scalar list result too.
    [Fact]
    public void Stored_procedure_tuple_with_a_scalar_list_result_compiles_cleanly()
    {
        var source = "#nullable enable\n" + Repo("""
                [StoredProcedure("get_names")]
                public partial Task<(List<string?> Names, int total)> GetNamesAsync(int total, CancellationToken ct);
            """);
        var (run, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source);

        Assert.Empty(run.Diagnostics);
        var problems = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                || (d.Severity == DiagnosticSeverity.Warning && d.Id.StartsWith("CS86", System.StringComparison.Ordinal)))
            .Select(d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}")
            .ToArray();
        Assert.True(problems.Length == 0, string.Join("; ", problems));
        var generated = string.Concat(run.GeneratedTrees.Select(t => t.ToString()));
        Assert.Contains("global::System.Collections.Generic.List<string?> __elem0 = default!;", generated, System.StringComparison.Ordinal);
    }
}
