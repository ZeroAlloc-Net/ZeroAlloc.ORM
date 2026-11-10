using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Tests.Model;

// A {name} placeholder in [Query] / [Command] SQL binds to a string member of the repository.
// The tests read the model the transform builds: the masked Sql, the spans and the members.
public class PlaceholderResolutionTests
{
    private const string Usings = """
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        """;

    private static string Repo(string header, string members, string sql = "SELECT COUNT(*) FROM {table}")
        => Usings + $$"""
            public sealed partial class Repo{{header}}
            {
            {{members}}
                [Query("{{sql}}")]
                public partial Task<int> CountAsync(CancellationToken ct);
            }
            """;

    internal static QueryRepositoryModel RunRepository(string source)
    {
        var compilation = GeneratorHarness.CreateCompilation(new[] { CSharpSyntaxTree.ParseText(source) });
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new ZeroAlloc.ORM.Generator.OrmGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        var result = driver.RunGenerators(compilation).GetRunResult();
        return result.Results[0].TrackedSteps["Repositories"]
            .SelectMany(s => s.Outputs)
            .Select(o => (QueryRepositoryModel)o.Value)
            .Single();
    }

    [Fact]
    public void Table_BindsToCapturedPrimaryConstructorParameter()
    {
        var repo = RunRepository(Repo("(IAsyncDbConnection connection, string table)", ""));

        var member = Assert.Single(repo.PlaceholderMembers);
        Assert.Equal(new PlaceholderMember("table", "table"), member);
        var method = Assert.Single(repo.Methods);
        Assert.Empty(method.Diagnostics);
        Assert.Equal("SELECT COUNT(*) FROM {table}", method.SqlTemplate);
        Assert.DoesNotContain('{', method.Sql);
        Assert.Equal("table", Assert.Single(method.SqlPlaceholders).Name);
    }

    [Fact]
    public void Table_BindsToField()
    {
        var repo = RunRepository(Repo("(IAsyncDbConnection connection)",
            "    private readonly string tableName = \"t\";\n    private readonly string table = \"t\";\n"));

        Assert.Equal(new PlaceholderMember("table", "table"), Assert.Single(repo.PlaceholderMembers));
        Assert.Empty(repo.Methods[0].Diagnostics);
    }

    [Fact]
    public void Table_BindsToGetOnlyProperty()
    {
        var repo = RunRepository(Repo("(IAsyncDbConnection connection)", "    private string Table { get; } = \"t\";\n"));

        Assert.Equal(new PlaceholderMember("table", "Table"), Assert.Single(repo.PlaceholderMembers));
        Assert.Empty(repo.Methods[0].Diagnostics);
    }

    [Fact]
    public void Table_IgnoresOneLeadingUnderscoreAndCase()
    {
        var repo = RunRepository(Repo("(IAsyncDbConnection connection)", "    private readonly string _Table = \"t\";\n"));

        Assert.Equal(new PlaceholderMember("table", "_Table"), Assert.Single(repo.PlaceholderMembers));
        Assert.Empty(repo.Methods[0].Diagnostics);
    }

    [Fact]
    public void Table_NoStringMember_ReportsZAO115()
    {
        var repo = RunRepository(Repo("(IAsyncDbConnection connection)", ""));

        var d = Assert.Single(repo.Methods[0].Diagnostics);
        Assert.Equal("ZAO115", d.DescriptorId);
        Assert.Empty(repo.PlaceholderMembers);
    }

    [Fact]
    public void Table_UncapturedConstructorParameter_ReportsZAO115()
    {
        var repo = RunRepository(Repo("", "    public Repo(IAsyncDbConnection connection, string table) { _c = connection; }\n    private readonly IAsyncDbConnection _c;\n"));

        var d = Assert.Single(repo.Methods[0].Diagnostics);
        Assert.Equal("ZAO115", d.DescriptorId);
        Assert.Contains(d.MessageArgs, a => a.Contains("capture", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Table_TwoCandidates_ReportsZAO115()
    {
        var repo = RunRepository(Repo("(IAsyncDbConnection connection)",
            "    private readonly string table = \"a\";\n    private readonly string _table = \"b\";\n"));

        var d = Assert.Single(repo.Methods[0].Diagnostics);
        Assert.Equal("ZAO115", d.DescriptorId);
        var text = string.Join(" ", d.MessageArgs);
        Assert.Contains("table", text, StringComparison.Ordinal);
        Assert.Contains("_table", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Table_NonStringMember_ReportsZAO115()
    {
        var repo = RunRepository(Repo("(IAsyncDbConnection connection)", "    private readonly int table = 1;\n"));

        var d = Assert.Single(repo.Methods[0].Diagnostics);
        Assert.Equal("ZAO115", d.DescriptorId);
        Assert.Contains(d.MessageArgs, a => a.Contains("int", StringComparison.Ordinal));
    }

    [Fact]
    public void Table_UsedTwiceWithoutMember_ReportsZAO115Once()
    {
        var repo = RunRepository(Repo("(IAsyncDbConnection connection)", "",
            "SELECT COUNT(*) FROM {table} JOIN {table} ON 1 = 1"));

        var d = Assert.Single(repo.Methods[0].Diagnostics);
        Assert.Equal("ZAO115", d.DescriptorId);
    }

    [Fact]
    public void ColumnPlaceholder_NeedsNoMember()
    {
        var repo = RunRepository(Repo("(IAsyncDbConnection connection)", "", "SELECT {col:Id} FROM T"));

        Assert.Empty(repo.Methods[0].Diagnostics);
        Assert.Empty(repo.PlaceholderMembers);
        Assert.Equal(SqlPlaceholderKind.Column, Assert.Single(repo.Methods[0].SqlPlaceholders).Kind);
    }

    [Theory]
    [InlineData("(IAsyncDbConnection connection, bool quoteColumns)", "", "quoteColumns")]
    [InlineData("(IAsyncDbConnection connection)", "    private readonly bool _QuoteColumns = true;", "_QuoteColumns")]
    [InlineData("(IAsyncDbConnection connection)", "    public static bool QuoteColumns { get; } = true;", "QuoteColumns")]
    public void QuoteColumns_BindsToABoolMember(string header, string members, string access)
    {
        var repo = RunRepository(Repo(header, members, "SELECT {col:Id} FROM T"));

        Assert.Equal(access, repo.QuoteColumnsAccess);
        Assert.Empty(repo.Methods[0].Diagnostics);
        Assert.Equal("ZAO118", Assert.Single(repo.Methods[0].DialectRequiredDiagnostics).DescriptorId);
    }

    [Fact]
    public void QuoteColumns_Absent_IsNull_AndMemberOnlyMethodHasNoZAO118()
    {
        Assert.Null(RunRepository(Repo("(IAsyncDbConnection connection)", "", "SELECT {col:Id} FROM T")).QuoteColumnsAccess);

        var memberOnly = RunRepository(Repo("(IAsyncDbConnection connection, string table, bool quoteColumns)", ""));
        Assert.Null(memberOnly.QuoteColumnsAccess);
        Assert.Empty(memberOnly.Methods[0].DialectRequiredDiagnostics);
    }

    [Fact]
    public void StoredProcedure_KeepsNoSpansNorTemplate_AndReportsZAO117()
    {
        var repo = RunRepository(Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection, string table)
            {
                [StoredProcedure("{table}_proc")]
                public partial Task<int> RunAsync(CancellationToken ct);
            }
            """);

        var method = Assert.Single(repo.Methods);
        Assert.Empty(method.SqlPlaceholders);
        Assert.Null(method.SqlTemplate);
        Assert.Equal("ZAO117", Assert.Single(method.Diagnostics).DescriptorId);
        Assert.Empty(repo.PlaceholderMembers);
    }

    [Fact]
    public void ExistingSqlWithQuotedBraces_UnchangedModel()
    {
        var repo = RunRepository(Repo("(IAsyncDbConnection connection)", "", "SELECT COUNT(*) FROM T WHERE a = '{1,2}'"));

        var method = repo.Methods[0];
        Assert.Equal("SELECT COUNT(*) FROM T WHERE a = '{1,2}'", method.Sql);
        Assert.Empty(method.SqlPlaceholders);
        Assert.Null(method.SqlTemplate);
        Assert.Empty(repo.PlaceholderMembers);
        Assert.Null(repo.QuoteColumnsAccess);
        Assert.Empty(method.Diagnostics);
    }
}
