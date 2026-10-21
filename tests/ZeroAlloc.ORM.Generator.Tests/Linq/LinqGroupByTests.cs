using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// [LinqQuery] GroupBy: the key renders as GROUP BY, and the Select over each group reads its
// key, its key members and its aggregates. A group always has rows, so a group aggregate needs
// no COALESCE, except a Sum over a nullable selector, whose values may all be null. Every
// snapshot is also compiled: no error anywhere and no warning in the emitted code.
public class LinqGroupByTests
{
    private const string Repo = """
        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class TaskStore(IAsyncDbConnection connection)
        {

        """;

    private const string Rows = """

        public sealed record StatusCount(WorkStatus Status, int Count);

        public sealed record KindTotals(WorkKind Kind, long Tasks, int? Priority, long Size, double AverageOrder, decimal? AveragePrice, int MinOrder, string? MaxNote, bool Flag, int Limit);

        public sealed record DayCount(DateTime Day, int Count);

        public sealed record NoteDayTokens(string? Note, DateTime Day, long Order, long Size, int Count);

        """;

    // One column as the key, g.Key projected as it, and every group aggregate: Count, LongCount,
    // a nullable Sum, which keeps COALESCE, a plain Sum, Average, Min and Max, next to a literal
    // and a parameter. `.Date` alone is a key too.
    [Fact]
    public void Group_by_single_column_with_count()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(CountByStatusQuery))]
                public partial Task<List<StatusCount>> CountByStatus(CancellationToken ct);

                [LinqQuery(nameof(TotalsByKindQuery))]
                public partial Task<List<KindTotals>> TotalsByKind(int limit, CancellationToken ct);

                [LinqQuery(nameof(CountByDayQuery))]
                public partial Task<List<DayCount>> CountByDay(CancellationToken ct);

                private static IQuery<StatusCount> CountByStatusQuery() =>
                    Sql.Table<TaskEntity>()
                        .GroupBy(t => t.Status)
                        .Select(g => new StatusCount(g.Key, g.Count()));

                private static IQuery<KindTotals> TotalsByKindQuery(int limit) =>
                    Sql.Table<TaskEntity>()
                        .GroupBy(t => t.Kind)
                        .Select(g => new KindTotals(
                            g.Key,
                            g.LongCount(),
                            g.Sum(t => t.Priority),
                            g.Sum(t => t.Size),
                            g.Average(t => t.Order),
                            g.Average(t => (decimal?)t.Price),
                            g.Min(t => t.Order),
                            g.Max(t => t.Note),
                            true,
                            limit));

                private static IQuery<DayCount> CountByDayQuery() =>
                    Sql.Table<TaskEntity>()
                        .GroupBy(t => t.LocalAt.Date)
                        .Select(g => new DayCount(g.Key, g.Count()));
            }
            """ + Rows);
    }

    // Daedalus' Q1: tokens per model and day. An anonymous key of a column and a `.Date`, each
    // member read back by name, after a WHERE; a DateTimeOffset column's `.Date` is a DateTime.
    [Fact]
    public void Group_by_anonymous_key_with_date()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(TokensPerNoteAndDayQuery))]
                public partial Task<List<NoteDayTokens>> TokensPerNoteAndDay(DateTimeOffset since, CancellationToken ct);

                private static IQuery<NoteDayTokens> TokensPerNoteAndDayQuery(DateTimeOffset since) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.CreatedAt >= since)
                        .GroupBy(t => new { t.Note, Day = t.CreatedAt.Date })
                        .Select(g => new NoteDayTokens(
                            g.Key.Note,
                            g.Key.Day,
                            g.Sum(t => (long)t.Order),
                            g.Sum(t => t.Size),
                            g.Count()));
            }
            """ + Rows);
    }

    // OrderBy and ThenBy over a key member and an aggregate render as ORDER BY over the key's
    // expression and the aggregate; Skip and Take page the groups.
    [Fact]
    public void Group_by_with_order_by_key_and_aggregate()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(BusiestDaysQuery))]
                public partial Task<List<NoteDayTokens>> BusiestDays(int skip, int take, CancellationToken ct);

                [LinqQuery(nameof(TopStatusQuery))]
                public partial Task<StatusCount?> TopStatus(CancellationToken ct);

                private static IQuery<NoteDayTokens> BusiestDaysQuery(int skip, int take) =>
                    Sql.Table<TaskEntity>()
                        .GroupBy(t => new { t.Note, Day = t.LocalAt.Date })
                        .OrderBy(g => g.Key.Day)
                        .ThenByDescending(g => g.Sum(t => t.Size))
                        .ThenBy(g => g.Key.Note)
                        .Skip(skip)
                        .Take(take)
                        .Select(g => new NoteDayTokens(g.Key.Note, g.Key.Day, g.Sum(t => (long)t.Order), g.Sum(t => t.Size), g.Count()));

                private static IQuery<StatusCount> TopStatusQuery() =>
                    Sql.Table<TaskEntity>()
                        .GroupBy(t => t.Status)
                        .OrderByDescending(g => g.Count())
                        .ThenBy(g => g.Key)
                        .Select(g => new StatusCount(g.Key, g.Count()));
            }
            """ + Rows);
    }

    // HAVING and counting the groups are not translated yet, nor is a key that is not a column,
    // a column's `.Date` or an anonymous type of those; nor reading an anonymous g.Key whole.
    [Theory]
    [InlineData("IQuery<StatusCount>", "Task<List<StatusCount>>", ".GroupBy(t => t.Status).Where(g => g.Count() > 1).Select(g => new StatusCount(g.Key, g.Count()))", "Where")]
    [InlineData("IQuery<int>", "Task<int>", ".GroupBy(t => t.Status).Count()", "Count")]
    [InlineData("IQuery<long>", "Task<long>", ".GroupBy(t => t.Status).LongCount()", "LongCount")]
    [InlineData("IQuery<bool>", "Task<bool>", ".GroupBy(t => t.Status).Any()", "Any")]
    [InlineData("IQuery<int>", "Task<int>", ".GroupBy(t => t.Status).Select(g => new StatusCount(g.Key, g.Count())).Count()", "Count")]
    [InlineData("IQuery<StatusCount>", "Task<List<StatusCount>>", ".GroupBy(t => t.Order + 1).Select(g => new StatusCount(WorkStatus.Open, g.Count()))", "t.Order + 1")]
    [InlineData("IQuery<StatusCount>", "Task<List<StatusCount>>", ".GroupBy(t => new { t.Status, Next = t.Order + 1 }).Select(g => new StatusCount(g.Key.Status, g.Count()))", "Next = t.Order + 1")]
    [InlineData("IQuery<StatusCount>", "Task<List<StatusCount>>", ".GroupBy(t => t.Status).Select(g => new StatusCount(g.Key, g.Count() + 1))", "g.Count() + 1")]
    [InlineData("IQuery<StatusCount>", "Task<List<StatusCount>>", ".Take(5).GroupBy(t => t.Status).Select(g => new StatusCount(g.Key, g.Count()))", "GroupBy")]
    [InlineData("IQuery<StatusCount>", "Task<List<StatusCount>>", ".OrderBy(t => t.Order).GroupBy(t => t.Status).Select(g => new StatusCount(g.Key, g.Count()))", "GroupBy")]
    [InlineData("IQuery<StatusCount>", "Task<List<StatusCount>>", ".GroupBy(t => t.Status).Select(g => new StatusCount(g.Key, g.Max(t => (int)t.Kind)))", "(int)t.Kind")]
    [InlineData("IQuery<StatusCount>", "Task<List<StatusCount>>", ".GroupBy(t => t.Status).OrderBy(g => 1).Select(g => new StatusCount(g.Key, g.Count()))", "1")]
    public void Having_and_group_count_report_ZAO102(string queryType, string returnType, string chain, string located)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(LinqWhereTests.Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial
            """ + " " + returnType + """
             Find(CancellationToken ct);

                private static
            """ + " " + queryType + """
             FindQuery() =>
                    Sql.Table<TaskEntity>()
            """ + chain + """
            ;
            }
            """ + Rows);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal(located, tree.ToString().Substring(span.Start, span.Length));
    }

    // Groups are not rows a method can return: a GroupBy must end with a Select.
    [Fact]
    public void Group_by_without_select_reports_ZAO107()
    {
        var (result, _) = GeneratorHarness.RunGeneratorOnFile(LinqWhereTests.Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<IGroup<WorkStatus, TaskEntity>>> Find(CancellationToken ct);

                private static IQuery<IGroup<WorkStatus, TaskEntity>> FindQuery() =>
                    Sql.Table<TaskEntity>().GroupBy(t => t.Status);
            }
            """ + Rows);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO107", diagnostic.Id);
        Assert.Contains("GroupBy", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static void VerifyCompiled(
        string body,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(LinqWhereTests.Prelude + body);

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

        GeneratorSnapshot.Verify(result, testFilePath, testMethod);
    }
}
