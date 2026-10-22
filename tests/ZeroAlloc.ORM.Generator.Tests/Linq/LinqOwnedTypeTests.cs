using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// EF owned types: an [OwnedMap] property's members are columns of the entity's table, named
// {Owner}_{Property} unless a dotted [ColumnMap] renames them. A query reads r.Repository.Url as
// that column wherever it reads a column: Where, Select, OrderBy, GroupBy keys, aggregates and
// join keys. A whole owned object is not a column, and a member of an object the map does not
// declare owned is not mapped.
public class LinqOwnedTypeTests
{
    private const string Prelude = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;
        using ZeroAlloc.ORM.Linq;

        namespace TestApp;

        public enum RepositoryPlatform { GitHub, GitLab }

        public sealed class RepositoryReference
        {
            public string Url { get; private set; } = "";
            public string? Branch { get; private set; }
            public string? CommitSha { get; private set; }
            public RepositoryPlatform Platform { get; private set; }
        }

        public sealed class CodeLocation
        {
            public string FilePath { get; private set; } = "";
            public int? StartLine { get; private set; }
            public int? EndLine { get; private set; }
        }

        public sealed class AnalysisOutcome
        {
            public string? PullRequestUrl { get; private set; }
            public string? ValidationResult { get; private set; }
            public bool HasFailedValidation { get; private set; }
        }

        public sealed class CodeAnalysisRequest
        {
            public Guid Id { get; private set; }
            public string Title { get; private set; } = "";
            public RepositoryReference Repository { get; private set; } = null!;
            public CodeLocation Location { get; private set; } = null!;
            public AnalysisOutcome? Outcome { get; private set; }
        }

        // Url and FilePath carry Daedalus' bare column names; every other member keeps EF's default.
        [TableMap(typeof(CodeAnalysisRequest), "AnalysisRequests")]
        [OwnedMap(nameof(CodeAnalysisRequest.Repository))]
        [OwnedMap(nameof(CodeAnalysisRequest.Location))]
        [OwnedMap(nameof(CodeAnalysisRequest.Outcome))]
        [ColumnMap("Repository.Url", Column = "RepositoryUrl")]
        [ColumnMap("Location.FilePath", Column = "FilePath")]
        internal static class CodeAnalysisRequestMap;

        public sealed class PullRequest
        {
            public Guid Id { get; private set; }
            public string RepositoryUrl { get; private set; } = "";
            public int Number { get; private set; }
        }

        [TableMap(typeof(PullRequest), "PullRequests")]
        internal static class PullRequestMap;

        public sealed record RequestLine(Guid Id, string Url, string? PullRequestUrl, int? StartLine);

        public sealed record PlatformCount(RepositoryPlatform Platform, int Count, int? LastLine);

        public sealed record RequestPull(string FilePath, int Number);

        """;

    // Each owned member reads its own column; Outcome is optional, and r.Outcome!.X reads the
    // column, NULL when the request has no outcome.
    [Fact]
    public void Owned_member_in_where_select_order()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class RequestStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(FailedQuery))]
                public partial Task<List<RequestLine>> Failed(RepositoryPlatform platform, string? branch, CancellationToken ct);

                private static IQuery<RequestLine> FailedQuery(RepositoryPlatform platform, string? branch) =>
                    Sql.Table<CodeAnalysisRequest>()
                        .Where(r => r.Repository.Platform == platform
                            && r.Repository.Branch == branch
                            && r.Outcome!.HasFailedValidation == true)
                        .OrderBy(r => r.Location.StartLine)
                        .ThenBy(r => r.Location.FilePath)
                        .Select(r => new RequestLine(r.Id, r.Repository.Url, r.Outcome!.PullRequestUrl, r.Location.StartLine));
            }
            """,
            "SELECT \"Id\", \"RepositoryUrl\", \"Outcome_PullRequestUrl\", \"Location_StartLine\" FROM \"AnalysisRequests\"",
            "ORDER BY \"Location_StartLine\" NULLS FIRST, \"FilePath\"",
            "\"Repository_Platform\" = @platform",
            "\"Outcome_HasFailedValidation\" = TRUE");
    }

    // A group key, an aggregate's value and a join key read the owned member's column too.
    [Fact]
    public void Owned_member_as_group_key_aggregate_and_join_key()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class RequestStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(ByPlatformQuery))]
                public partial Task<List<PlatformCount>> ByPlatform(CancellationToken ct);

                [LinqQuery(nameof(PullsQuery))]
                public partial Task<List<RequestPull>> Pulls(CancellationToken ct);

                private static IQuery<PlatformCount> ByPlatformQuery() =>
                    Sql.Table<CodeAnalysisRequest>()
                        .GroupBy(r => r.Repository.Platform)
                        .Select(g => new PlatformCount(g.Key, g.Count(), g.Max(r => r.Location.EndLine)));

                private static IQuery<RequestPull> PullsQuery() =>
                    Sql.Table<CodeAnalysisRequest>()
                        .Join(Sql.Table<PullRequest>(), r => r.Repository.Url, p => p.RepositoryUrl, (r, p) => new { Request = r, Pull = p })
                        .Select(x => new RequestPull(x.Request.Location.FilePath, x.Pull.Number));
            }
            """,
            "GROUP BY \"Repository_Platform\"",
            "max(\"Location_EndLine\")",
            "t0.\"RepositoryUrl\" = t1.\"RepositoryUrl\"",
            "SELECT t0.\"FilePath\", t1.\"Number\"");
    }

    // A whole owned object maps to several columns, while a projected row reads one column per
    // constructor parameter.
    [Fact]
    public void Whole_owned_object_reports_ZAO107()
    {
        var (d, located) = Single(Prelude + """
            public sealed record WithRepository(Guid Id, RepositoryReference Repository);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class RequestStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(AllQuery))]
                public partial Task<List<WithRepository>> All(CancellationToken ct);

                private static IQuery<WithRepository> AllQuery() =>
                    Sql.Table<CodeAnalysisRequest>().Select(r => new WithRepository(r.Id, r.Repository));
            }
            """);

        Assert.Equal("ZAO107", d.Id);
        Assert.Equal("r.Repository", located);
        Assert.Contains("Repository", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // The map declares only Repository owned: Location's members are not mapped.
    [Fact]
    public void Member_of_an_undeclared_owned_type_reports_ZAO105()
    {
        var (d, located) = Single(Prelude.Replace(
            "[OwnedMap(nameof(CodeAnalysisRequest.Location))]",
            string.Empty,
            StringComparison.Ordinal).Replace(
            "[ColumnMap(\"Location.FilePath\", Column = \"FilePath\")]",
            string.Empty,
            StringComparison.Ordinal) + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class RequestStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(AllQuery))]
                public partial Task<List<RequestLine>> All(CancellationToken ct);

                private static IQuery<RequestLine> AllQuery() =>
                    Sql.Table<CodeAnalysisRequest>()
                        .Select(r => new RequestLine(r.Id, r.Repository.Url, null, r.Location.StartLine));
            }
            """);

        Assert.Equal("ZAO105", d.Id);
        Assert.Equal("r.Location.StartLine", located);
        Assert.Contains("Location.StartLine", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static (Diagnostic Diagnostic, string Located) Single(string source)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(source);
        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Same(tree, diagnostic.Location.SourceTree);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        var span = diagnostic.Location.SourceSpan;
        return (diagnostic, tree.ToString().Substring(span.Start, span.Length));
    }

    // Each of `expected` is SQL the generated files must contain, checked before the snapshot.
    private static void VerifyCompiled(
        string source,
        string expected1,
        string expected2,
        string expected3,
        string expected4,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source);

        Assert.Empty(result.Results[0].Diagnostics);
        var generatedPaths = result.GeneratedTrees.Select(t => t.FilePath).ToHashSet(StringComparer.Ordinal);
        var problems = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                || (d.Severity == DiagnosticSeverity.Warning
                    && d.Location.SourceTree is { } tree
                    && generatedPaths.Contains(tree.FilePath)))
            .Select(d => d.Id + ": " + d.GetMessage(CultureInfo.InvariantCulture))
            .ToList();
        Assert.Empty(problems);

        // The SQL sits in C# string literals, which escape its quoted identifiers.
        var generated = string.Concat(result.GeneratedTrees.Select(t => t.ToString()))
            .Replace("\\\"", "\"", StringComparison.Ordinal);
        Assert.Contains(expected1, generated, StringComparison.Ordinal);
        Assert.Contains(expected2, generated, StringComparison.Ordinal);
        Assert.Contains(expected3, generated, StringComparison.Ordinal);
        Assert.Contains(expected4, generated, StringComparison.Ordinal);

        GeneratorSnapshot.Verify(result, testFilePath, testMethod);
    }
}
