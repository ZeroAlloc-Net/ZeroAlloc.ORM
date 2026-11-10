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

    private static string ValueRepo(string sql, string extraParams = "")
        => ColumnUsings + $$"""
            [Dialect(SqlDialect.Sqlite)]
            public sealed partial class Repo(IAsyncDbConnection connection, string table)
            {
                [Query("{{sql.Replace("\n", "\\n", System.StringComparison.Ordinal)}}")]
                public partial Task<int> GetAsync({{extraParams}}CancellationToken ct);
            }
            """;

    private static int Count(string source, string id)
        => Ids(source).Count(x => string.Equals(x, id, System.StringComparison.Ordinal));

    [Theory]
    [InlineData("SELECT COUNT(*) FROM T WHERE a = {table}")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a=\t{table}")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a < {table}")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a > {table}")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a <= {table}")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a >= {table}")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a <> {table}")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a != {table}")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a LIKE {table}")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a like {table}")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a IN ({table}")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a IN ( {table}, 1)")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a = {col:Id}")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a IN({table}")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a NOT IN\n({table}, 1)")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a = /* c */ {table}")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a = -- c\n{table}")]
    public void Placeholder_AfterComparisonOperator_ReportsZAO116(string sql)
    {
        var source = ValueRepo(sql);
        var d = Assert.Single(GeneratorHarness.RunGenerator(source).Results[0].Diagnostics, x => string.Equals(x.Id, "ZAO116", System.StringComparison.Ordinal));
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, d.Severity);
        var message = d.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(sql.Contains("{col:Id}", System.StringComparison.Ordinal) ? "{col:Id}" : "{table}", message, System.StringComparison.Ordinal);
        Assert.Contains("@", message, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("INSERT INTO T VALUES ({table}, @b)")]
    [InlineData("INSERT INTO T VALUES (@a, {table})")]
    [InlineData("INSERT INTO T(a, b) VALUES (@a, @b), (@c, {table})")]
    [InlineData("INSERT INTO T VALUES(@a, {table})")]
    // A dot after the placeholder exempts it only when the dot follows it.
    [InlineData("INSERT INTO T VALUES (@a, {table}) ON CONFLICT DO NOTHING")]
    public void Placeholder_InsideValuesTuple_ReportsZAO116(string sql)
        => Assert.Equal(1, Count(ValueRepo(sql, "int a, int b, int c, "), "ZAO116"));

    [Fact]
    public void Placeholder_NameMatchesUsedParameter_ReportsZAO116()
        => Assert.Equal(1, Count(ValueRepo("SELECT COUNT(*) FROM T WHERE a = @table AND b IN (SELECT 1 FROM {table})", "string table, "), "ZAO116"));

    [Fact]
    public void Placeholder_NameMatchesUsedColonParameter_ReportsZAO116()
        => Assert.Equal(1, Count(ValueRepo("SELECT COUNT(*) FROM {table} WHERE a = :table"), "ZAO116"));

    [Theory]
    [InlineData("SELECT COUNT(*) FROM {table}")]
    [InlineData("SELECT COUNT(*) FROM T JOIN {table} ON 1 = 1")]
    [InlineData("UPDATE {table} SET a = 1")]
    [InlineData("INSERT INTO {table} (a) VALUES (@a)")]
    [InlineData("DELETE FROM {table} WHERE a = @a")]
    [InlineData("TRUNCATE TABLE {table}")]
    [InlineData("SELECT COUNT(*) FROM T t WHERE t.{col:Id} = @a")]
    [InlineData("SELECT {col:Id} FROM T WHERE a = @a")]
    [InlineData("SELECT a FROM T ORDER BY {col:Id}")]
    [InlineData("SELECT a FROM T WHERE {col:Id} = @a")]
    [InlineData("SELECT a FROM T WHERE a = @a AND b = 'x = {table}'")]
    [InlineData("INSERT INTO {table} ({col:Id}, {col:Name}) VALUES (@a, @b)")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a ALIKE {table}")]
    [InlineData("SELECT COUNT(*) FROM T JOIN ({table}) x ON 1 = 1")]
    [InlineData("SELECT COUNT(*) FROM T WHERE a = @a -- x =\n AND b IN (SELECT 1 FROM {table})")]
    [InlineData("INSERT INTO T VALUES (@a, COALESCE({table}, 1))")]
    // A placeholder followed by a dot qualifies a name, so it is never a value; whitespace
    // before the dot does not change that, as SQL allows it there.
    [InlineData("SELECT COUNT(*) FROM Orders o JOIN {table} ON o.Id = {table}.OrderId")]
    [InlineData("SELECT COUNT(*) FROM Orders o JOIN {table} ON o.Id = {table} .OrderId")]
    [InlineData("SELECT COUNT(*) FROM Orders o JOIN T ON o.Id = {col:Id}.OrderId")]
    [InlineData("SELECT COUNT(*) FROM Orders o WHERE o.Id IN ({table}.OrderId)")]
    // MySQL upsert: VALUES(...) right after = is a function call, not a VALUES tuple.
    [InlineData("INSERT INTO {table} ({col:Id}) VALUES (@a) ON DUPLICATE KEY UPDATE {col:Id} = VALUES({col:Id})")]
    [InlineData("INSERT INTO {table} ({col:Id}) VALUES (@a) ON DUPLICATE KEY UPDATE {col:Id} = VALUES ({col:Id})")]
    public void Placeholder_InIdentifierPosition_NoZAO116(string sql)
        => Assert.Equal(0, Count(ValueRepo(sql, "int a, int b, "), "ZAO116"));

    [Fact]
    public void Placeholder_ColonColumnPlaceholder_IsNotAParameter()
        => Assert.Equal(0, Count(ValueRepo("SELECT {col:Id} FROM T"), "ZAO116"));

    [Fact]
    public void Placeholder_SameTextTwice_ReportsZAO116Once()
        => Assert.Equal(1, Count(ValueRepo("SELECT 1 FROM T WHERE a = {table} OR b = {table}"), "ZAO116"));

    [Fact]
    public void Placeholder_TwoDistinctTexts_ReportsZAO116ForEach()
    {
        var source = ColumnUsings + """
            [Dialect(SqlDialect.Sqlite)]
            public sealed partial class Repo(IAsyncDbConnection connection, string table, string other)
            {
                [Query("SELECT 1 FROM T WHERE a = {table} OR b = {other}")]
                public partial Task<int> GetAsync(CancellationToken ct);
            }
            """;
        Assert.Equal(2, Count(source, "ZAO116"));
    }

    [Fact]
    public void Placeholder_InValuePosition_PreventsEmit()
        => Assert.Empty(GeneratorHarness.RunGenerator(ValueRepo("SELECT 1 FROM T WHERE a = {table}")).Results[0].GeneratedSources);

    [Fact]
    public void ColumnPlaceholder_NamedLikeUsedParameter_NoZAO116()
    {
        var insert = "INSERT INTO {table} ({col:Id}, {col:Name}) VALUES (@id, @name)";
        Assert.Equal(0, Count(ValueRepo(insert, "int id, string name, "), "ZAO116"));
        Assert.Equal(0, Count(ValueRepo("SELECT {col:Id} FROM {table} WHERE {col:Id} = @id", "int id, "), "ZAO116"));
    }

    [Fact]
    public void MemberPlaceholder_NamedLikeUsedParameter_StillReportsZAO116()
        => Assert.Equal(1, Count(ValueRepo("SELECT 1 FROM {table} WHERE x = @table", "string table, "), "ZAO116"));

    [Fact]
    public void Placeholder_AfterNestedCallInValuesTuple_IsAKnownFalseNegative()
        => Assert.Equal(0, Count(ValueRepo("INSERT INTO T VALUES (@a, COALESCE({table}, 1))", "int a, "), "ZAO116"));

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "docs", "diagnostics", "ZAO116.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new System.InvalidOperationException("repo root not found");
    }

    // The first csharp block after the placeholder heading must generate without a diagnostic, so
    // the documented example cannot drift into a build failure.
    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/cookbook/commands.md")]
    [InlineData("docs/cookbook/flat-row.md")]
    public void DocsPlaceholderExample_GeneratesWithoutDiagnostics(string file)
    {
        var text = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), file));
        var heading = text.IndexOf("## Table and column placeholders", System.StringComparison.Ordinal);
        Assert.True(heading >= 0);
        var open = text.IndexOf("```csharp\n", heading, System.StringComparison.Ordinal);
        Assert.True(open >= 0);
        var start = open + "```csharp\n".Length;
        var block = text.Substring(start, text.IndexOf("```", start, System.StringComparison.Ordinal) - start);

        var result = GeneratorHarness.RunGenerator(ColumnUsings + "public sealed record OrderRow(int Id, string Name);\n\n" + block);

        Assert.Empty(result.Results[0].Diagnostics);
        Assert.NotEmpty(result.Results[0].GeneratedSources);
    }
}
