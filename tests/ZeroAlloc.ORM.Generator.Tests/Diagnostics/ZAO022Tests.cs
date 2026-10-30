using System.Globalization;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// ZAO022 reports a return type that passes the outer surface check (Task<T>,
// ValueTask<T>, IAsyncEnumerable<T>) but that [Query] cannot materialize, e.g.
// Task<HashSet<TRow>>. It is an error (#400): an Info diagnostic was hidden by the
// build output, so the adopter saw only the downstream CS8795. The message ends in a
// reason that names the alternative.
//
// Note: List<T>, IList<T>, IReadOnlyList<T> at the top level are SUPPORTED shapes
// (issue #102 + v1.3.1 follow-up). HashSet<T> is genuinely unsupported and stays
// useful as the ZAO022 canary shape.
public class ZAO022Tests
{
    [Fact]
    public void Unknown_shape_Task_of_HashSet_emits_ZAO022()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT id FROM orders")]
                public partial Task<HashSet<OrderRow>> GetOrdersAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO022", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Supported_ScalarInt_shape_does_not_emit_ZAO022()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial Task<int> GetOneAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO022", System.StringComparison.Ordinal));
    }

    private const string Prelude = """
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public sealed record Row(System.Guid Id, string Name);

        public readonly record struct Code(int Value);

        """;

    private static Diagnostic SingleZao022(string members)
    {
        var source = Prelude + "public sealed partial class Repo(IAsyncDbConnection connection)\n{\n" + members + "\n}\n";
        var diagnostics = GeneratorHarness.RunGenerator(source).Results[0].Diagnostics;
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ZAO022", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Descriptor.DefaultSeverity);
        Assert.DoesNotContain("v0.1", diagnostic.Descriptor.Title.ToString(CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
        return diagnostic;
    }

    [Fact]
    public void Non_nullable_single_row_is_an_error_that_points_at_the_nullable_row()
    {
        var diagnostic = SingleZao022("""
                [Query("SELECT id, name FROM t WHERE k = @k")]
                public partial Task<Row> GetRowAsync(int k, CancellationToken ct);
            """);

        Assert.Equal(
            "Method 'GetRowAsync' returns 'System.Threading.Tasks.Task<TestApp.Row>', which [Query] cannot materialize: "
                + "a single row must be nullable; use Task<TestApp.Row?>, which returns null when the result set is empty",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Non_nullable_single_row_in_a_ValueTask_names_the_ValueTask()
    {
        var diagnostic = SingleZao022("""
                [Query("SELECT id, name FROM t WHERE k = @k")]
                public partial ValueTask<Row> GetRowAsync(int k, CancellationToken ct);
            """);

        Assert.EndsWith(
            "a single row must be nullable; use ValueTask<TestApp.Row?>, which returns null when the result set is empty",
            diagnostic.GetMessage(CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Array_of_rows_is_an_error_that_points_at_the_list_shapes()
    {
        var diagnostic = SingleZao022("""
                [Query("SELECT id, name FROM t")]
                public partial Task<Row[]> GetRowsAsync(CancellationToken ct);
            """);

        Assert.Equal(
            "Method 'GetRowsAsync' returns 'System.Threading.Tasks.Task<TestApp.Row[]>', which [Query] cannot materialize: "
                + "an array return reads one PostgreSQL array column, and 'TestApp.Row' is no array element; "
                + "return rows as List<T>, IReadOnlyList<T> or IList<T>, or stream them as IAsyncEnumerable<T>",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void IEnumerable_of_rows_is_an_error_that_points_at_the_list_shapes()
    {
        var diagnostic = SingleZao022("""
                [Query("SELECT id, name FROM t")]
                public partial Task<IEnumerable<Row>> GetRowsAsync(CancellationToken ct);
            """);

        Assert.Equal(
            "Method 'GetRowsAsync' returns 'System.Threading.Tasks.Task<System.Collections.Generic.IEnumerable<TestApp.Row>>', "
                + "which [Query] cannot materialize: 'System.Collections.Generic.IEnumerable<TestApp.Row>' is not a supported "
                + "collection; return List<T>, IReadOnlyList<T> or IList<T>, or stream rows as IAsyncEnumerable<T>",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void List_of_an_unreadable_element_is_an_error_that_names_the_element()
    {
        var diagnostic = SingleZao022("""
                [Query("SELECT link FROM t")]
                public partial Task<List<System.Uri>> GetLinksAsync(CancellationToken ct);
            """);

        Assert.EndsWith(
            "the list element 'System.Uri' is neither a row type nor a supported scalar; use a row record whose "
                + "constructor parameters each bind to a column, or a scalar element such as int, string or Guid",
            diagnostic.GetMessage(CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Other_collections_point_at_the_list_shapes()
    {
        var diagnostic = SingleZao022("""
                [Query("SELECT id, name FROM t")]
                public partial Task<HashSet<Row>> GetRowsAsync(CancellationToken ct);
            """);

        Assert.EndsWith(
            "'System.Collections.Generic.HashSet<TestApp.Row>' is not a supported collection; return List<T>, "
                + "IReadOnlyList<T> or IList<T>, or stream rows as IAsyncEnumerable<T>",
            diagnostic.GetMessage(CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_tuple_with_an_unsupported_element_names_the_element_shapes()
    {
        var diagnostic = SingleZao022("""
                [Query("SELECT id, name FROM t; SELECT id, name FROM t")]
                public partial Task<(int Count, Row[] Rows)> GetRowsAsync(CancellationToken ct);
            """);

        Assert.EndsWith(
            "a tuple of result sets takes one element per result set, each a scalar, a row, or a list of rows or scalars",
            diagnostic.GetMessage(CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Other_shapes_get_the_list_of_supported_shapes()
    {
        var diagnostic = SingleZao022("""
                [Query("SELECT code FROM t")]
                public partial Task<Code> GetCodeAsync(CancellationToken ct);
            """);

        Assert.EndsWith(
            "supported shapes are a scalar or enum such as Task<Guid> or Task<Guid?>, a nullable row Task<TRow?>, "
                + "a list of rows or scalars as List<T>, IReadOnlyList<T> or IList<T>, a tuple of result sets, "
                + "a PostgreSQL array T[], or IAsyncEnumerable<TRow>",
            diagnostic.GetMessage(CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }
}
