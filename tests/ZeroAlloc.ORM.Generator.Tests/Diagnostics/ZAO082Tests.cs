using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// ZAO082 fires when a parameter uses a type only Npgsql can bind, here a
// PostgreSQL array, and the compilation does not reference Npgsql.
public class ZAO082Tests
{
    private const string ArrayParameterSource = """
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT COUNT(*) FROM posts WHERE tags && @tags")]
            public partial Task<int> CountTaggedAsync(string[] tags, CancellationToken ct);
        }
        """;

    [Fact]
    public void Array_parameter_without_npgsql_reports_ZAO082()
    {
        var result = GeneratorHarness.RunGenerator(ArrayParameterSource);
        var diagnostics = result.Results[0].Diagnostics;

        var zao082 = Assert.Single(diagnostics, d => string.Equals(d.Id, "ZAO082", System.StringComparison.Ordinal));
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, zao082.Severity);
        Assert.Equal(
            "Parameter or column 'tags' uses a PostgreSQL array, which needs Npgsql. "
            + "Reference the Npgsql package, or bind it another way.",
            zao082.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        // The array is classified, so the generic no-binding-strategy error stays quiet.
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO041", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Array_parameter_with_npgsql_reports_nothing()
    {
        var (result, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(ArrayParameterSource, NpgsqlStub.Source);

        Assert.Empty(result.Results[0].Diagnostics);
        Assert.Empty(compileDiagnostics);
    }

    [Fact]
    public void Bulk_insert_collection_keeps_its_meaning()
    {
        // The row collection and the identity list are collections too, but neither is a
        // PostgreSQL array: the bulk-insert shape check runs first and claims them.
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total) RETURNING Id", Kind = CommandKind.BulkInsert)]
                public partial Task<IReadOnlyList<int>> InsertOrdersAsync(IReadOnlyList<OrderRow> orders, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);

        Assert.Empty(result.Results[0].Diagnostics);
    }
}
