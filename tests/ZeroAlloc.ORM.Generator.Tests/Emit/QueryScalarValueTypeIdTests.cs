using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// #429 — a value-type typed id or value object at [Query] scalar position, Task<OrderId>,
// Task<OrderId?> and List<OrderId>, used to report ZAO022. It reads its one column through
// its convention, the constructor or From factory, as a tuple scalar element does. A class
// one keeps the row path, which returns null for no row. In a list a row still wins, so a
// non-null record struct such as List<OrderId> reads as a one-column row, as it did before.
public class QueryScalarValueTypeIdTests
{
    private const string Header = """
        using System;
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public readonly record struct OrderId(int Value);

        public readonly struct Sku
        {
            private Sku(string value) => Value = value;

            public string Value { get; }

            public static Sku From(string value) => new(value);
        }

        public sealed record CustomerId(Guid Value);

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
        """;

    private static string Repo(string members) => Header + "\n" + members + "\n}\n";

    [Fact]
    public void Query_returning_struct_id_reads_first_column_through_its_constructor()
    {
        var source = Repo("""
                [Query("SELECT id FROM t WHERE k = @k")]
                public partial Task<OrderId> GetIdAsync(int k, CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Query_returning_nullable_struct_id_maps_no_row_and_NULL_to_null()
    {
        var source = Repo("""
                [Query("SELECT id FROM t WHERE k = @k")]
                public partial Task<OrderId?> GetIdAsync(int k, CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Query_returning_List_of_struct_value_object_reads_every_row_through_From()
    {
        var source = Repo("""
                [Query("SELECT sku FROM t ORDER BY k")]
                public partial Task<List<Sku>> GetSkusAsync(CancellationToken ct);
            """);
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    // Every shape compiles, with no generator diagnostic, no compile error and no nullable
    // warning in the emitted code. A class id still reads through the row path.
    [Fact]
    public void Every_struct_id_shape_compiles_cleanly()
    {
        var source = "#nullable enable\n" + Repo("""
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<OrderId> IdAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial ValueTask<OrderId> IdValueTaskAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<OrderId?> MaybeIdAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial ValueTask<Sku?> MaybeSkuAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<Sku> SkuAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t")] public partial Task<List<OrderId>> IdListAsync(CancellationToken ct);
                [Query("SELECT v FROM t")] public partial Task<IReadOnlyList<OrderId?>> MaybeIdListAsync(CancellationToken ct);
                [Query("SELECT v FROM t")] public partial Task<IList<Sku>> SkuListAsync(CancellationToken ct);
                [Query("SELECT v FROM t; SELECT v FROM t")] public partial Task<(OrderId Head, List<OrderId?> Ids)> IdsAsync(CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<CustomerId?> CustomerAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t")] public partial Task<List<CustomerId>> CustomersAsync(CancellationToken ct);
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
        Assert.Contains("new global::TestApp.OrderId(__reader.GetInt32(0))", generated, System.StringComparison.Ordinal);
        Assert.Contains("global::TestApp.Sku.From(__reader.GetString(0))", generated, System.StringComparison.Ordinal);
    }

    // A class id is a row, so its non-nullable form keeps pointing at Task<TId?>.
    [Fact]
    public void Non_nullable_class_id_still_reports_ZAO022_pointing_at_the_nullable_row()
    {
        var source = Repo("""
                [Query("SELECT v FROM t WHERE k = @k")]
                public partial Task<CustomerId> CustomerAsync(int k, CancellationToken ct);
            """);
        var diagnostic = Assert.Single(GeneratorHarness.RunGenerator(source).Results[0].Diagnostics);

        Assert.Equal("ZAO022", diagnostic.Id);
        Assert.EndsWith(
            "a single row must be nullable; use Task<TestApp.CustomerId?>, which returns null when the result set is empty",
            diagnostic.GetMessage(CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }

    // An explicit [Materialize(Factory)] keeps winning over the convention, nullable or not.
    [Fact]
    public void Materialize_factory_wins_over_the_struct_id_convention()
    {
        var source = """
            #nullable enable
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            [Materialize(Factory = nameof(FromStorage))]
            public readonly record struct OrderId(int Value)
            {
                public static OrderId FromStorage(int raw) => new(raw * 10);
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<OrderId> IdAsync(int k, CancellationToken ct);
                [Query("SELECT v FROM t WHERE k = @k")] public partial Task<OrderId?> MaybeIdAsync(int k, CancellationToken ct);
            }
            """;
        var (run, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source);

        // The factory path's own ZAO050 warning on the nullable form is expected.
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(compilation.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        var generated = string.Concat(run.GeneratedTrees.Select(t => t.ToString()));
        Assert.Equal(2, CountOccurrences(generated, "OrderId.FromStorage("));
        Assert.DoesNotContain("new global::TestApp.OrderId(", generated, System.StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, System.StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, System.StringComparison.Ordinal))
            count++;
        return count;
    }
}
