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
    private const string ColumnRepoBody = """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT {col:Id} FROM T")]
            public partial Task<int> GetAsync(CancellationToken ct);

            [Query("SELECT {col:Name} FROM T")]
            public partial Task<int> GetOtherAsync(CancellationToken ct);
        }
        """;

    private const string ColumnUsings = """
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        """;

    [Fact]
    public void ColumnPlaceholder_NoDialect_ReportsZAO118()
    {
        var result = GeneratorHarness.RunGenerator(ColumnUsings + ColumnRepoBody);

        var diagnostics = result.Results[0].Diagnostics.Where(x => string.Equals(x.Id, "ZAO118", System.StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, d.Severity));
        Assert.Contains("{col:Id}", diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
        Assert.Empty(result.Results[0].GeneratedSources);
    }

    [Fact]
    public void ColumnPlaceholder_DialectDeclaredOnType_Ok()
    {
        Assert.DoesNotContain(Ids(ColumnUsings + "[Dialect(SqlDialect.MySql)]\n" + ColumnRepoBody), id => string.Equals(id, "ZAO118", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ColumnPlaceholder_DialectDeclaredOnAssembly_Ok()
    {
        var source = ColumnUsings.Replace("namespace TestApp;", "[assembly: Dialect(SqlDialect.Sqlite)]\n\nnamespace TestApp;", System.StringComparison.Ordinal) + ColumnRepoBody;

        var result = GeneratorHarness.RunGenerator(source);

        Assert.Empty(result.Results[0].Diagnostics);
        Assert.Single(result.Results[0].GeneratedSources);
    }

    [Fact]
    public void MemberPlaceholderOnly_NoDialect_ReportsNoZAO118()
    {
        var source = ColumnUsings + """
            public sealed partial class Repo(IAsyncDbConnection connection, string table)
            {
                [Query("SELECT COUNT(*) FROM {table}")]
                public partial Task<int> CountAsync(CancellationToken ct);
            }
            """;

        Assert.DoesNotContain(Ids(source), id => string.Equals(id, "ZAO118", System.StringComparison.Ordinal));
    }

    [Fact]
    public void QuoteColumns_OfAnotherType_ReportsZAO115()
    {
        var source = ColumnUsings + """
            [Dialect(SqlDialect.Sqlite)]
            public sealed partial class Repo(IAsyncDbConnection connection, string quoteColumns)
            {
                [Query("SELECT {col:Id} FROM T")]
                public partial Task<int> GetAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);

        var d = Assert.Single(result.Results[0].Diagnostics, x => string.Equals(x.Id, "ZAO115", System.StringComparison.Ordinal));
        var message = d.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("{col:Id}", message, System.StringComparison.Ordinal);
        Assert.Contains("quoteColumns", message, System.StringComparison.Ordinal);
        Assert.Contains("bool", message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteColumns_Ambiguous_ReportsZAO115()
    {
        var source = ColumnUsings + """
            [Dialect(SqlDialect.Sqlite)]
            public sealed partial class Repo(IAsyncDbConnection connection, bool quoteColumns)
            {
                private readonly bool _quoteColumns = quoteColumns;

                [Query("SELECT {col:Id} FROM T")]
                public partial Task<int> GetAsync(CancellationToken ct);
            }
            """;

        var d = Assert.Single(GeneratorHarness.RunGenerator(source).Results[0].Diagnostics, x => string.Equals(x.Id, "ZAO115", System.StringComparison.Ordinal));
        Assert.Contains("more than one", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }
}
