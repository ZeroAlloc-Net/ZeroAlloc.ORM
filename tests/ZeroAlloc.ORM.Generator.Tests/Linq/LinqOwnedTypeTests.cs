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

    // A plain column whose type is an object, a Uri? the map does not declare owned, is still
    // tested as itself, as in 2.6.0: the map, not the type's shape, decides what is owned.
    internal const string Websites = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;
        using ZeroAlloc.ORM.Linq;

        namespace TestApp;

        public sealed class Company
        {
            public Guid Id { get; private set; }
            public string Name { get; private set; } = "";
            public Uri? Website { get; private set; }
        }

        [TableMap(typeof(Company), "Companies")]
        internal static class CompanyMap;

        public sealed record CompanyLine(Guid Id, string Name);

        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class CompanyStore(IAsyncDbConnection connection)
        {
            [LinqQuery(nameof(WithWebsiteQuery))]
            public partial Task<List<CompanyLine>> WithWebsite(CancellationToken ct);

            [LinqQuery(nameof(WithoutWebsiteQuery))]
            public partial Task<List<CompanyLine>> WithoutWebsite(CancellationToken ct);

            private static IQuery<CompanyLine> WithWebsiteQuery() =>
                Sql.Table<Company>().Where(c => c.Website != null).Select(c => new CompanyLine(c.Id, c.Name));

            private static IQuery<CompanyLine> WithoutWebsiteQuery() =>
                Sql.Table<Company>().Where(c => c.Website == null).Select(c => new CompanyLine(c.Id, c.Name));
        }
        """;

    [Fact]
    public void Plain_object_column_null_test_renders_as_in_2_6_0()
    {
        VerifyCompiled(Websites,
            "SELECT \"Id\", \"Name\" FROM \"Companies\" WHERE \"Website\" IS NOT NULL",
            "SELECT \"Id\", \"Name\" FROM \"Companies\" WHERE \"Website\" IS NULL",
            "WithWebsite(",
            "WithoutWebsite(");
    }

    // An owned positional record is an owned object like any other: its members read their
    // columns, its null test checks every column and a whole projection of it is ZAO107. The
    // composite rule, ZAO102, is for a composite the map does not declare owned.
    private const string Reviewed = """
            public sealed record Review(string? Reviewer, bool Approved);

            public sealed class ReviewedRequest
            {
                public Guid Id { get; private set; }
                public Review? Review { get; private set; }
            }

            [TableMap(typeof(ReviewedRequest), "ReviewedRequests")]
            [OwnedMap(nameof(ReviewedRequest.Review))]
            internal static class ReviewedRequestMap;

            public sealed record ReviewLine(Guid Id, string? Reviewer);

            """;

    [Fact]
    public void Owned_record_members_and_null_tests_translate()
    {
        VerifyCompiled(Prelude + Reviewed + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class ReviewStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(ReviewedQuery))]
                public partial Task<List<ReviewLine>> Reviewed(CancellationToken ct);

                [LinqQuery(nameof(UnreviewedQuery))]
                public partial Task<List<ReviewLine>> Unreviewed(CancellationToken ct);

                private static IQuery<ReviewLine> ReviewedQuery() =>
                    Sql.Table<ReviewedRequest>()
                        .Where(r => r.Review != null && r.Review!.Approved)
                        .Select(r => new ReviewLine(r.Id, r.Review!.Reviewer));

                private static IQuery<ReviewLine> UnreviewedQuery() =>
                    Sql.Table<ReviewedRequest>()
                        .Where(r => r.Review == null)
                        .Select(r => new ReviewLine(r.Id, r.Review!.Reviewer));
            }
            """,
            "SELECT \"Id\", \"Review_Reviewer\" FROM \"ReviewedRequests\"",
            "WHERE NOT (\"Review_Reviewer\" IS NULL AND \"Review_Approved\" IS NULL) AND \"Review_Approved\"",
            "WHERE (\"Review_Reviewer\" IS NULL AND \"Review_Approved\" IS NULL)",
            "Unreviewed(");
    }

    [Fact]
    public void Whole_owned_record_reports_ZAO107()
    {
        var (d, located) = Single(Prelude + Reviewed + """
            public sealed record WithReview(Guid Id, Review? Review);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class ReviewStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(AllQuery))]
                public partial Task<List<WithReview>> All(CancellationToken ct);

                private static IQuery<WithReview> AllQuery() =>
                    Sql.Table<ReviewedRequest>().Select(r => new WithReview(r.Id, r.Review));
            }
            """);

        Assert.Equal("ZAO107", d.Id);
        Assert.Equal("r.Review", located);
        Assert.Contains("whole owned 'Review'", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // Owned members inside an IN subquery bind through the subquery's own scope: a null test on
    // the optional Outcome and an EnumAsString owned member compared with an enum parameter,
    // once in a subquery without a join and once in one with a join, aliased s0 and s1.
    private const string Pulls = """
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
            public RepositoryPlatform Platform { get; private set; }
        }

        public sealed class AnalysisOutcome
        {
            public string? PullRequestUrl { get; private set; }
            public bool HasFailedValidation { get; private set; }
        }

        public sealed class Request
        {
            public Guid Id { get; private set; }
            public Guid TeamId { get; private set; }
            public RepositoryReference Repository { get; private set; } = null!;
            public AnalysisOutcome? Outcome { get; private set; }
        }

        public sealed class Team
        {
            public Guid Id { get; private set; }
            public string Name { get; private set; } = "";
        }

        public sealed class PullRequest
        {
            public Guid Id { get; private set; }
            public string RepositoryUrl { get; private set; } = "";
            public int Number { get; private set; }
        }

        [TableMap(typeof(Request), "Requests")]
        [OwnedMap(nameof(Request.Repository))]
        [OwnedMap(nameof(Request.Outcome))]
        [ColumnMap("Repository.Url", Column = "RepositoryUrl")]
        [ColumnMap("Repository.Platform", EnumAsString = true)]
        internal static class RequestMap;

        [TableMap(typeof(Team), "Teams")]
        internal static class TeamMap;

        [TableMap(typeof(PullRequest), "PullRequests")]
        internal static class PullRequestMap;

        public sealed record PullLine(Guid Id, int Number);

        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class PullStore(IAsyncDbConnection connection)
        {
            [LinqQuery(nameof(AnalyzedQuery))]
            public partial Task<List<PullLine>> Analyzed(RepositoryPlatform platform, CancellationToken ct);

            [LinqQuery(nameof(AnalyzedByTeamQuery))]
            public partial Task<List<PullLine>> AnalyzedByTeam(RepositoryPlatform platform, string team, CancellationToken ct);

            private static IQuery<PullLine> AnalyzedQuery(RepositoryPlatform platform) =>
                Sql.Table<PullRequest>()
                    .Where(p => Sql.Table<Request>()
                        .Where(r => r.Outcome != null && r.Repository.Platform == platform)
                        .Select(r => r.Repository.Url)
                        .Contains(p.RepositoryUrl))
                    .Select(p => new PullLine(p.Id, p.Number));

            private static IQuery<PullLine> AnalyzedByTeamQuery(RepositoryPlatform platform, string team) =>
                Sql.Table<PullRequest>()
                    .Where(p => Sql.Table<Request>()
                        .Join(Sql.Table<Team>(), r => r.TeamId, t => t.Id, (r, t) => new { Request = r, Team = t })
                        .Where(y => y.Request.Outcome != null && y.Request.Repository.Platform == platform && y.Team.Name == team)
                        .Select(y => y.Request.Repository.Url)
                        .Contains(p.RepositoryUrl))
                    .Select(p => new PullLine(p.Id, p.Number));
        }
        """;

    [Fact]
    public void Owned_members_inside_a_subquery()
    {
        VerifyCompiled(Pulls,
            "\"RepositoryUrl\" IN (SELECT \"RepositoryUrl\" FROM \"Requests\" WHERE NOT (\"Outcome_PullRequestUrl\" IS NULL AND \"Outcome_HasFailedValidation\" IS NULL) AND \"Repository_Platform\" = @platform)",
            "\"RepositoryUrl\" IN (SELECT s0.\"RepositoryUrl\" FROM \"Requests\" AS s0 INNER JOIN \"Teams\" AS s1 ON s0.\"TeamId\" = s1.\"Id\" WHERE NOT (s0.\"Outcome_PullRequestUrl\" IS NULL AND s0.\"Outcome_HasFailedValidation\" IS NULL) AND s0.\"Repository_Platform\" = @platform AND s1.\"Name\" = @team)",
            "__p_platform.DbType = global::System.Data.DbType.String;",
            "__p_platform.Value = @platform.ToString();");
    }

    // Only the parameter a whole object feeds is left to the binder: another composite parameter
    // of the same row still reports at the return type that it reads several columns.
    [Fact]
    public void Other_composite_parameter_still_reports_at_the_return_type()
    {
        var (d, located) = Single(Prelude + Reviewed + """
            public sealed record Money(decimal Amount, string Currency);

            public sealed record WithReview(Guid Id, Review Review, Money Price);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class ReviewStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(AllQuery))]
                public partial Task<List<WithReview>> All(CancellationToken ct);

                private static IQuery<WithReview> AllQuery() =>
                    Sql.Table<ReviewedRequest>().Select(r => new WithReview(r.Id, r.Review!, null!));
            }
            """);

        Assert.Equal("ZAO107", d.Id);
        Assert.Equal("Task<List<WithReview>>", located);
        Assert.Contains("Price", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
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
    // with the diagnostic of the position it is in and one shared reason.
    [Theory]
    [InlineData("Where(r => r.Repository.Origin.FilePath == \"x\")", "r.Id, r.Repository.Url", "ZAO102", "r.Repository.Origin.FilePath")]
    [InlineData("OrderBy(r => r.Repository.Origin.StartLine)", "r.Id, r.Repository.Url", "ZAO102", "r.Repository.Origin.StartLine")]
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
        Assert.Contains("a type nested in the owned 'Repository'; [OwnedMap] maps one level", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // A navigation to another entity is no owned object: its paths are not mapped, ZAO105, in a
    // condition, a null test and a projection alike, also through an optional navigation, which
    // is not lifted as an optional owned object would be.
    [Theory]
    [InlineData("Where(e => e.Task.Owner == null)", "e.Id, e.Name", "e.Task.Owner", "Task.Owner")]
    [InlineData("Where(e => e.Task.Title == \"x\")", "e.Id, e.Name", "e.Task.Title", "Task.Title")]
    [InlineData("Where(e => true)", "e.Id, e.Task.Title", "e.Task.Title", "Task.Title")]
    [InlineData("Where(e => true)", "e.Id, e.Parent!.Title", "e.Parent!.Title", "Parent.Title")]
    public void Navigation_path_reports_ZAO105(string filter, string projection, string expectedLocation, string expectedPath)
    {
        var (d, located) = Single("""
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;
            using ZeroAlloc.ORM.Linq;

            namespace TestApp;

            public sealed class Person
            {
                public Guid Id { get; private set; }
                public string Name { get; private set; } = "";
            }

            public sealed class WorkItem
            {
                public Guid Id { get; private set; }
                public string Title { get; private set; } = "";
                public Person Owner { get; private set; } = null!;
            }

            public sealed class Execution
            {
                public Guid Id { get; private set; }
                public string Name { get; private set; } = "";
                public WorkItem Task { get; private set; } = null!;
                public WorkItem? Parent { get; private set; }
            }

            [TableMap(typeof(Execution), "Executions")]
            [ColumnMap(nameof(Execution.Task), Ignore = true)]
            [ColumnMap(nameof(Execution.Parent), Ignore = true)]
            internal static class ExecutionMap;

            public sealed record Line(Guid Id, string Name);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class ExecutionStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(AllQuery))]
                public partial Task<List<Line>> All(CancellationToken ct);

                private static IQuery<Line> AllQuery() =>
                    Sql.Table<Execution>().FILTER.Select(e => new Line(PROJECTION));
            }
            """.Replace("FILTER", filter, StringComparison.Ordinal).Replace("PROJECTION", projection, StringComparison.Ordinal));

        Assert.Equal("ZAO105", d.Id);
        Assert.Equal(expectedLocation, located);
        Assert.Contains("'" + expectedPath + "'", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private const string Homepages = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;
        using ZeroAlloc.ORM.Linq;

        namespace TestApp;

        public sealed class Homepage
        {
            public Uri? Link { get; private set; }
            public string Title { get; private set; } = "";
        }

        public sealed class Company
        {
            public Guid Id { get; private set; }
            public Homepage? Home { get; private set; }
        }

        [TableMap(typeof(Company), "Companies")]
        [OwnedMap(nameof(Company.Home))]
        internal static class CompanyMap;

        public sealed record CompanyLine(Guid Id, string? Title);


        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class CompanyStore(IAsyncDbConnection connection)
        {
            [LinqQuery(nameof(LinkedQuery))]
            public partial Task<List<CompanyLine>> Linked(CancellationToken ct);

            [LinqQuery(nameof(UnlinkedQuery))]
            public partial Task<List<CompanyLine>> Unlinked(CancellationToken ct);

            private static IQuery<CompanyLine> LinkedQuery() =>
                Sql.Table<Company>().Where(c => c.Home!.Link != null).Select(c => new CompanyLine(c.Id, c.Home!.Title));

            private static IQuery<CompanyLine> UnlinkedQuery() =>
                Sql.Table<Company>().Where(c => c.Home!.Link == null).Select(c => new CompanyLine(c.Id, c.Home!.Title));
        }
        """;

    // An owned member whose type is itself an object with one column, a Uri, is the column the
    // map gives it: its null test is "Home_Link" IS [NOT] NULL, and it projects as that column.
    [Fact]
    public void Owned_member_of_an_object_type_is_its_column()
    {
        VerifyCompiled(Homepages,
            "SELECT \"Id\", \"Home_Title\" FROM \"Companies\" WHERE \"Home_Link\" IS NOT NULL",
            "SELECT \"Id\", \"Home_Title\" FROM \"Companies\" WHERE \"Home_Link\" IS NULL",
            "Linked(",
            "Unlinked(");
    }

    // Projected, it binds as its column: the one diagnostic left is the materializer's own ZAO022
    // for a Uri, which no row type reads today, plain column or owned.
    [Fact]
    public void Owned_member_of_an_object_type_projects_as_its_column()
    {
        var (result, _) = GeneratorHarness.RunGeneratorOnFile(Homepages.Replace(
            "public sealed record CompanyLine(Guid Id, string? Title);",
            "public sealed record CompanyLine(Guid Id, string? Title);\n\npublic sealed record LinkLine(Guid Id, Uri? Link);",
            StringComparison.Ordinal).Replace(
            "    [LinqQuery(nameof(LinkedQuery))]",
            "    [LinqQuery(nameof(LinksQuery))]\n    public partial Task<List<LinkLine>> Links(CancellationToken ct);\n\n"
                + "    private static IQuery<LinkLine> LinksQuery() =>\n"
                + "        Sql.Table<Company>().Select(c => new LinkLine(c.Id, c.Home!.Link));\n\n"
                + "    [LinqQuery(nameof(LinkedQuery))]",
            StringComparison.Ordinal));

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO022", diagnostic.Id);
        Assert.Contains("Links", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
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
    // Npgsql is referenced, as a DateTime compared with a declared timestamptz column binds as one.
    private static void VerifyCompiled(
        string source,
        string expected1,
        string expected2,
        string expected3,
        string expected4,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source, NpgsqlStub.Source);

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
