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

    // A member of the optional Outcome is NULL when the request has none, so it is lifted to
    // bool? and compensated as a nullable column: C# would throw on the null Outcome, SQL reads
    // the row as not failed. A whole owner's null test checks every one of its columns, for a
    // required owner too.
    [Fact]
    public void Optional_owned_member_and_owner_null_tests_are_compensated()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class RequestStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(NotFailedQuery))]
                public partial Task<List<RequestLine>> NotFailed(CancellationToken ct);

                [LinqQuery(nameof(NotTrueQuery))]
                public partial Task<List<RequestLine>> NotTrue(CancellationToken ct);

                [LinqQuery(nameof(WithoutOutcomeQuery))]
                public partial Task<List<RequestLine>> WithoutOutcome(CancellationToken ct);

                [LinqQuery(nameof(WithRepositoryQuery))]
                public partial Task<List<RequestLine>> WithRepository(CancellationToken ct);

                private static IQuery<RequestLine> NotFailedQuery() =>
                    Sql.Table<CodeAnalysisRequest>()
                        .Where(r => !r.Outcome!.HasFailedValidation)
                        .Select(r => new RequestLine(r.Id, r.Repository.Url, r.Outcome!.PullRequestUrl, r.Location.StartLine));

                private static IQuery<RequestLine> NotTrueQuery() =>
                    Sql.Table<CodeAnalysisRequest>()
                        .Where(r => r.Outcome!.HasFailedValidation != true)
                        .Select(r => new RequestLine(r.Id, r.Repository.Url, r.Outcome!.PullRequestUrl, r.Location.StartLine));

                private static IQuery<RequestLine> WithoutOutcomeQuery() =>
                    Sql.Table<CodeAnalysisRequest>()
                        .Where(r => r.Outcome == null)
                        .Select(r => new RequestLine(r.Id, r.Repository.Url, r.Outcome!.PullRequestUrl, r.Location.StartLine));

                private static IQuery<RequestLine> WithRepositoryQuery() =>
                    Sql.Table<CodeAnalysisRequest>()
                        .Where(r => r.Repository != null)
                        .Select(r => new RequestLine(r.Id, r.Repository.Url, r.Outcome!.PullRequestUrl, r.Location.StartLine));
            }
            """,
            "WHERE (\"Outcome_HasFailedValidation\") IS NOT TRUE",
            "WHERE (\"Outcome_HasFailedValidation\" <> TRUE OR \"Outcome_HasFailedValidation\" IS NULL)",
            "WHERE (\"Outcome_PullRequestUrl\" IS NULL AND \"Outcome_ValidationResult\" IS NULL AND \"Outcome_HasFailedValidation\" IS NULL)",
            "WHERE NOT (\"RepositoryUrl\" IS NULL AND \"Repository_Branch\" IS NULL AND \"Repository_CommitSha\" IS NULL AND \"Repository_Platform\" IS NULL)");
    }

    // A non-nullable parameter cannot take the NULL an absent optional owned object reads.
    [Fact]
    public void Optional_owned_member_into_a_non_nullable_parameter_reports_ZAO107()
    {
        var (d, located) = Single(Prelude + """
            public sealed record Flag(Guid Id, bool Failed);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class RequestStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(FlagsQuery))]
                public partial Task<List<Flag>> Flags(CancellationToken ct);

                private static IQuery<Flag> FlagsQuery() =>
                    Sql.Table<CodeAnalysisRequest>().Select(r => new Flag(r.Id, r.Outcome!.HasFailedValidation));
            }
            """);

        Assert.Equal("ZAO107", d.Id);
        Assert.Equal("r.Outcome!.HasFailedValidation", located);
        Assert.Contains("'bool?'", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private const string Jobs = """
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

        public enum Cadence { Hourly, Daily }

        public sealed class Schedule
        {
            public Cadence Cadence { get; private set; }
            public DateTime NextRunAt { get; private set; }
        }

        public sealed class Job
        {
            public Guid Id { get; private set; }
            public Schedule Schedule { get; private set; } = null!;
        }

        [TableMap(typeof(Job), "Jobs")]
        [OwnedMap(nameof(Job.Schedule))]
        [ColumnMap("Schedule.Cadence", EnumAsString = true)]
        [ColumnMap("Schedule.NextRunAt", DateTimeStorage = DateTimeStorage.TimestampTz)]
        internal static class JobMap;

        public sealed record JobLine(Guid Id);

        public sealed record DayCount(DateTime Day, int Count);

        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class JobStore(IAsyncDbConnection connection)
        {
            [LinqQuery(nameof(DueQuery))]
            public partial Task<List<JobLine>> Due(Cadence cadence, DateTime before, CancellationToken ct);

            [LinqQuery(nameof(ByDayQuery))]
            public partial Task<List<DayCount>> ByDay(DateTime before, CancellationToken ct);

            private static IQuery<JobLine> DueQuery(Cadence cadence, DateTime before) =>
                Sql.Table<Job>()
                    .Where(j => j.Schedule.Cadence == cadence && j.Schedule.NextRunAt < before)
                    .Select(j => new JobLine(j.Id));

            private static IQuery<DayCount> ByDayQuery(DateTime before) =>
                Sql.Table<Job>()
                    .Where(j => j.Schedule.NextRunAt < before)
                    .GroupBy(j => j.Schedule.NextRunAt.Date)
                    .Select(g => new DayCount(g.Key, g.Count()));
        }
        """;

    // The rule is for values the owned branch lifts. A plain `string?` column projected into a
    // `string` parameter is accepted, as before owned types: nullable annotations decide only
    // C# warnings there, and a NULL fails at run time as it always has.
    [Fact]
    public void Nullable_reference_column_into_a_non_nullable_parameter_is_still_accepted()
    {
        var (result, _) = GeneratorHarness.RunGeneratorOnFile(Prelude + """
            public sealed class Note
            {
                public Guid Id { get; private set; }
                public string? Text { get; private set; }
            }

            [TableMap(typeof(Note), "Notes")]
            internal static class NoteMap;

            public sealed record NoteLine(Guid Id, string Text);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class NoteStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(NotesQuery))]
                public partial Task<List<NoteLine>> Notes(CancellationToken ct);

                [LinqQuery(nameof(UnsuppressedQuery))]
                public partial Task<List<NoteLine>> Unsuppressed(CancellationToken ct);

                private static IQuery<NoteLine> NotesQuery() =>
                    Sql.Table<Note>().Select(n => new NoteLine(n.Id, n.Text!));

                private static IQuery<NoteLine> UnsuppressedQuery() =>
                    Sql.Table<Note>().Select(n => new NoteLine(n.Id, n.Text));
            }
            """);

        Assert.Empty(result.Results[0].Diagnostics);
        Assert.Contains(
            result.GeneratedTrees,
            t => t.ToString().Contains("SELECT \\\"Id\\\", \\\"Text\\\" FROM \\\"Notes\\\"", StringComparison.Ordinal));
    }

    // The same rule holds for a group key: an absent Outcome is a group of its own, whose key is
    // NULL, so g.Key needs a bool? parameter.
    [Fact]
    public void Optional_owned_group_key_into_a_non_nullable_parameter_reports_ZAO107()
    {
        var (d, located) = Single(Prelude + """
            public sealed record FailedCount(bool Failed, int Count);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class RequestStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(CountsQuery))]
                public partial Task<List<FailedCount>> Counts(CancellationToken ct);

                private static IQuery<FailedCount> CountsQuery() =>
                    Sql.Table<CodeAnalysisRequest>()
                        .GroupBy(r => r.Outcome!.HasFailedValidation)
                        .Select(g => new FailedCount(g.Key, g.Count()));
            }
            """);

        Assert.Equal("ZAO107", d.Id);
        Assert.Equal("g.Key", located);
        Assert.Contains("'bool?'", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Optional_owned_group_key_into_a_nullable_parameter_translates()
    {
        VerifyCompiled(Prelude + """
            public sealed record FailedCount(bool? Failed, int Count);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class RequestStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(CountsQuery))]
                public partial Task<List<FailedCount>> Counts(CancellationToken ct);

                private static IQuery<FailedCount> CountsQuery() =>
                    Sql.Table<CodeAnalysisRequest>()
                        .GroupBy(r => r.Outcome!.HasFailedValidation)
                        .OrderBy(g => g.Key)
                        .Select(g => new FailedCount(g.Key, g.Count()));
            }
            """,
            "SELECT \"Outcome_HasFailedValidation\", count(*)::int",
            "GROUP BY \"Outcome_HasFailedValidation\"",
            "ORDER BY \"Outcome_HasFailedValidation\" NULLS FIRST",
            "FROM \"AnalysisRequests\"");
    }

    // An owned member's own [ColumnMap] settings apply as on a plain column: an EnumAsString
    // member compared with an enum parameter binds the parameter by name, with no ZAO111, and a
    // TimestampTz member's .Date, which only a GroupBy key reads, is cut at the UTC day.
    [Fact]
    public void Owned_EnumAsString_and_TimestampTz_members_follow_their_column_settings()
    {
        VerifyCompiled(Jobs,
            "WHERE \"Schedule_Cadence\" = @cadence AND \"Schedule_NextRunAt\" < @before",
            "date_trunc('day', \"Schedule_NextRunAt\", 'UTC')",
            "__p_cadence.DbType = global::System.Data.DbType.String;",
            "FROM \"Jobs\"");
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

    // An owned type nested in an owned type is not mapped: Repository's own CodeLocation and Spot
    // members are no columns the chain can read. Each use is a build error, never a silent column,
    // with the diagnostic of the position it is in.
    [Theory]
    [InlineData("Where(r => r.Repository.Origin.FilePath == \"x\")", "r.Id, r.Repository.Url", "ZAO102", "r.Repository.Origin.FilePath")]
    [InlineData("OrderBy(r => r.Repository.Origin.StartLine)", "r.Id, r.Repository.Url", "ZAO102", "r.Repository.Origin.StartLine")]
    [InlineData("Where(r => r.Repository.Origin == null)", "r.Id, r.Repository.Url", "ZAO105", "r.Repository.Origin")]
    [InlineData("Where(r => true)", "r.Id, r.Repository.Origin.FilePath", "ZAO107", "r.Repository.Origin.FilePath")]
    [InlineData("Where(r => true)", "r.Id, r.Repository.Spot.FilePath", "ZAO107", "r.Repository.Spot.FilePath")]
    public void Member_of_a_type_nested_in_an_owned_type_is_a_build_error(
        string filter, string projection, string expectedId, string expectedLocation)
    {
        var (d, located) = Single(Prelude.Replace(
            "public RepositoryPlatform Platform { get; private set; }",
            "public RepositoryPlatform Platform { get; private set; }\n"
                + "    public CodeLocation Origin { get; private set; } = null!;\n"
                + "    public Spot Spot { get; private set; } = null!;",
            StringComparison.Ordinal) + """
            public sealed record Spot(string FilePath, int Line);

            public sealed record UrlLine(Guid Id, string Url);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class RequestStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(AllQuery))]
                public partial Task<List<UrlLine>> All(CancellationToken ct);

                private static IQuery<UrlLine> AllQuery() =>
                    Sql.Table<CodeAnalysisRequest>().FILTER.Select(r => new UrlLine(PROJECTION));
            }
            """.Replace("FILTER", filter, StringComparison.Ordinal).Replace("PROJECTION", projection, StringComparison.Ordinal));

        Assert.Equal(expectedId, d.Id);
        Assert.Equal(expectedLocation, located);
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
