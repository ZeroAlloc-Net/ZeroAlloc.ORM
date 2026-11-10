using System.Linq;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// ZAO115 and ZAO117 as the build reports them. The model-level cases are in
// PlaceholderResolutionTests.
public class PlaceholderDiagnosticsTests
{
    private static string[] Ids(string source)
        => GeneratorHarness.RunGenerator(source).Results[0].Diagnostics.Select(d => d.Id).ToArray();

    [Fact]
    public void MissingMember_ReportsZAO115_AndEmitsNothing()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT COUNT(*) FROM {table}")]
                public partial Task<int> CountAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);

        var d = Assert.Single(result.Results[0].Diagnostics, x => string.Equals(x.Id, "ZAO115", System.StringComparison.Ordinal));
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("{table}", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
        Assert.Empty(result.Results[0].GeneratedSources);
    }

    [Fact]
    public void Placeholder_InBulkInsert_ReportsZAO117()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record Row(int Id, string Name);

            public sealed partial class Repo(IAsyncDbConnection connection, string table)
            {
                [Command("INSERT INTO {table}(Id, Name) VALUES (@Id, @Name)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertAsync(IReadOnlyList<Row> rows, CancellationToken ct);
            }
            """;

        Assert.Contains(Ids(source), id => string.Equals(id, "ZAO117", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Placeholder_InStoredProcedureName_ReportsZAO117()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection, string table)
            {
                [StoredProcedure("{table}_proc")]
                public partial Task<int> RunAsync(CancellationToken ct);
            }
            """;

        Assert.Contains(Ids(source), id => string.Equals(id, "ZAO117", System.StringComparison.Ordinal));
    }

    [Fact]
    public void NoPlaceholder_ReportsNeitherDiagnostic()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT COUNT(*) FROM T WHERE a = '{1,2}'")]
                public partial Task<int> CountAsync(CancellationToken ct);
            }
            """;

        Assert.DoesNotContain(Ids(source), id => string.Equals(id, "ZAO115", System.StringComparison.Ordinal) || string.Equals(id, "ZAO117", System.StringComparison.Ordinal));
    }
}
