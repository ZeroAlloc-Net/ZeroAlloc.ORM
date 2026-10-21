using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// [LinqQuery] ordering, paging, keyset row comparisons and the Count, LongCount and Any
// terminals. Every snapshot is also compiled: no error anywhere and no warning in the
// emitted code.
public class LinqOrderPagingTests
{
    private const string Repo = """
        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class TaskStore(IAsyncDbConnection connection)
        {

        """;

    // OrderBy, ThenBy and their Descending forms. Kind is stored by name, so it orders by the
    // stored text; that is documented, not translated.
    [Fact]
    public void Order_then_by_desc()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(OrderedQuery))]
                public partial Task<List<TaskSummary>> Ordered(CancellationToken ct);

                [LinqQuery(nameof(NewestQuery))]
                public partial IAsyncEnumerable<TaskSummary> Newest([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct);

                private static IQuery<TaskSummary> OrderedQuery() =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.IsActive)
                        .OrderBy(t => t.CreatedAt)
                        .ThenByDescending(t => t.Order)
                        .ThenBy(t => t.Kind)
                        .Select(t => new TaskSummary(t.Id, t.Title));

                private static IQuery<TaskSummary> NewestQuery() =>
                    Sql.Table<TaskEntity>()
                        .OrderByDescending(t => t.CreatedAt)
                        .ThenByDescending(t => t.Id)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    // Skip and Take take a parameter, clamped at 0 as LINQ reads a negative count, or an int
    // literal. The single-row shape reads one row at most: a Take literal becomes min(n, 1), a
    // Take parameter is capped at 1 too, and a Skip alone keeps LIMIT 1.
    [Fact]
    public void Skip_take_parameters()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(PageQuery))]
                public partial Task<List<TaskSummary>> Page(int skip, int take, CancellationToken ct);

                [LinqQuery(nameof(TopQuery))]
                public partial Task<List<TaskSummary>> Top(CancellationToken ct);

                [LinqQuery(nameof(NthQuery))]
                public partial Task<TaskSummary?> Nth(int skip, CancellationToken ct);

                [LinqQuery(nameof(TopQuery))]
                public partial Task<TaskSummary?> FirstOfTop(CancellationToken ct);

                [LinqQuery(nameof(PageQuery))]
                public partial Task<TaskSummary?> FirstOfPage(int skip, int take, CancellationToken ct);

                private static IQuery<TaskSummary> PageQuery(int skip, int take) =>
                    Sql.Table<TaskEntity>()
                        .OrderBy(t => t.Order)
                        .Skip(skip)
                        .Take(take)
                        .Select(t => new TaskSummary(t.Id, t.Title));

                private static IQuery<TaskSummary> TopQuery() =>
                    Sql.Table<TaskEntity>()
                        .OrderByDescending(t => t.Order)
                        .Take(20)
                        .Select(t => new TaskSummary(t.Id, t.Title));

                private static IQuery<TaskSummary> NthQuery(int skip) =>
                    Sql.Table<TaskEntity>()
                        .OrderBy(t => t.Order)
                        .Skip(skip)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    // Sql.Row(columns) compared with Sql.Row(parameters or literals): a PostgreSQL row comparison.
    // Each pair is a column and a value, in either order.
    [Fact]
    public void Keyset_row_comparison_two_and_three_columns()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(AfterQuery))]
                public partial Task<List<TaskSummary>> After(DateTimeOffset afterAt, Guid afterId, int take, CancellationToken ct);

                [LinqQuery(nameof(BeforeQuery))]
                public partial Task<List<TaskSummary>> Before(int order, Guid id, CancellationToken ct);

                [LinqQuery(nameof(AtLeastQuery))]
                public partial Task<List<TaskSummary>> AtLeast(DateTimeOffset at, CancellationToken ct);

                [LinqQuery(nameof(UpToQuery))]
                public partial Task<List<TaskSummary>> UpTo(long size, CancellationToken ct);

                private static IQuery<TaskSummary> AfterQuery(DateTimeOffset afterAt, Guid afterId, int take) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => Sql.Row(t.CreatedAt, t.Id) > Sql.Row(afterAt, afterId))
                        .OrderBy(t => t.CreatedAt)
                        .ThenBy(t => t.Id)
                        .Take(take)
                        .Select(t => new TaskSummary(t.Id, t.Title));

                private static IQuery<TaskSummary> BeforeQuery(int order, Guid id) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.IsActive && Sql.Row(t.Status, t.Order, t.Id) < Sql.Row(WorkStatus.Done, order, id))
                        .OrderByDescending(t => t.Status)
                        .ThenByDescending(t => t.Order)
                        .ThenByDescending(t => t.Id)
                        .Select(t => new TaskSummary(t.Id, t.Title));

                private static IQuery<TaskSummary> AtLeastQuery(DateTimeOffset at) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => Sql.Row(at, 3) <= Sql.Row(t.CreatedAt, t.Order))
                        .OrderBy(t => t.CreatedAt)
                        .Select(t => new TaskSummary(t.Id, t.Title));

                private static IQuery<TaskSummary> UpToQuery(long size) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => Sql.Row(t.Size, t.Title, t.Order) >= Sql.Row(size, "m", 0))
                        .OrderBy(t => t.Size)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
    }

    // Count, LongCount and Any, as Task or ValueTask of int, long and bool. A terminal after a
    // projection drops it. The ORDER BY goes too: it changes neither a count nor an existence.
    // A transaction parameter passes through as for [Query].
    [Fact]
    public void Count_long_count_any()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(CountQuery))]
                public partial Task<int> CountOpen(int min, CancellationToken ct);

                [LinqQuery(nameof(CountQuery))]
                public partial ValueTask<int> CountOpenInTransaction(int min, IAsyncDbTransaction tx, CancellationToken ct);

                [LinqQuery(nameof(LongCountQuery))]
                public partial Task<long> CountAll(CancellationToken ct);

                [LinqQuery(nameof(AnyQuery))]
                public partial Task<bool> AnyFeature(string title, CancellationToken ct);

                [LinqQuery(nameof(AnyQuery))]
                public partial ValueTask<bool> AnyFeatureNow(string title, CancellationToken ct);

                private static IQuery<int> CountQuery(int min) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Status == WorkStatus.Open && t.Order >= min)
                        .Count();

                private static IQuery<long> LongCountQuery() =>
                    Sql.Table<TaskEntity>()
                        .OrderBy(t => t.Order)
                        .Select(t => new TaskSummary(t.Id, t.Title))
                        .LongCount();

                private static IQuery<bool> AnyQuery(string title) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Kind == WorkKind.Feature && t.Title.StartsWith(title))
                        .Any();
            }
            """);
    }

    // A counted chain with Skip or Take counts the page: over a subquery for Count and
    // LongCount, and with the paging inside the EXISTS for Any.
    [Fact]
    public void Count_and_any_over_a_page_count_the_page()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(CountPageQuery))]
                public partial Task<int> CountPage(int skip, int take, CancellationToken ct);

                [LinqQuery(nameof(LongCountPageQuery))]
                public partial Task<long> LongCountPage(CancellationToken ct);

                [LinqQuery(nameof(AnyPageQuery))]
                public partial Task<bool> AnyPage(int skip, CancellationToken ct);

                private static IQuery<int> CountPageQuery(int skip, int take) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.IsActive)
                        .OrderBy(t => t.Order)
                        .Skip(skip)
                        .Take(take)
                        .Count();

                private static IQuery<long> LongCountPageQuery() =>
                    Sql.Table<TaskEntity>().Take(100).LongCount();

                private static IQuery<bool> AnyPageQuery(int skip) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.IsActive)
                        .Skip(skip)
                        .Select(t => new TaskSummary(t.Id, t.Title))
                        .Any();
            }
            """);
    }

    // A terminal drops its projection, and with it the only use of 'label': the method keeps the
    // parameter, but binds no DbParameter for it.
    [Fact]
    public void Terminal_binds_no_parameter_its_dropped_projection_used()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(CountQuery))]
                public partial Task<int> CountLabeled(string label, int min, CancellationToken ct);

                private static IQuery<int> CountQuery(string label, int min) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Order >= min)
                        .Select(t => new TaskSummary(t.Id, label))
                        .Count();
            }
            """);
    }

    // ZAO110 is a warning: the method is still generated, with the SQL as written.
    [Fact]
    public void Skip_without_order_warns_ZAO110()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(LinqWhereTests.Prelude + Repo + """
                [LinqQuery(nameof(PageQuery))]
                public partial Task<List<TaskSummary>> Page(int skip, CancellationToken ct);

                private static IQuery<TaskSummary> PageQuery(int skip) =>
                    Sql.Table<TaskEntity>().Where(t => t.IsActive).Skip(skip).Take(10).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO110", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Same(tree, diagnostic.Location.SourceTree);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("Skip", tree.ToString().Substring(span.Start, span.Length));
        Assert.Equal(
            "Skip or Take without OrderBy returns rows in no defined order",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
        VerifySnapshot(result);
    }

    [Fact]
    public void Take_without_order_on_a_stream_warns_ZAO110()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(LinqWhereTests.Prelude + Repo + """
                [LinqQuery(nameof(TopQuery))]
                public partial IAsyncEnumerable<TaskSummary> Top([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct);

                private static IQuery<TaskSummary> TopQuery() =>
                    Sql.Table<TaskEntity>().Take(10).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO110", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("Take", tree.ToString().Substring(span.Start, span.Length));
        Assert.Single(result.Results[0].GeneratedSources);
    }

    // "The row after N others" depends on the order as much as a page does.
    [Fact]
    public void Skip_on_a_single_row_without_order_warns_ZAO110()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(LinqWhereTests.Prelude + Repo + """
                [LinqQuery(nameof(PageQuery))]
                public partial Task<TaskSummary?> Nth(int skip, CancellationToken ct);

                private static IQuery<TaskSummary> PageQuery(int skip) =>
                    Sql.Table<TaskEntity>().Skip(skip).Take(3).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO110", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("Skip", tree.ToString().Substring(span.Start, span.Length));
        Assert.Single(result.Results[0].GeneratedSources);
    }

    // The single-row shape without a Skip reads any one row, and a terminal does not depend on
    // the order at all, so neither warns.
    [Fact]
    public void Single_row_and_terminals_without_order_do_not_warn()
    {
        var (result, _) = GeneratorHarness.RunGeneratorOnFile(LinqWhereTests.Prelude + Repo + """
                [LinqQuery(nameof(AnyQuery))]
                public partial Task<TaskSummary?> AnyOne(CancellationToken ct);

                [LinqQuery(nameof(TakeQuery))]
                public partial Task<TaskSummary?> OneOfTop(int take, CancellationToken ct);

                [LinqQuery(nameof(CountQuery))]
                public partial Task<int> CountPage(CancellationToken ct);

                private static IQuery<TaskSummary> AnyQuery() =>
                    Sql.Table<TaskEntity>().Select(t => new TaskSummary(t.Id, t.Title));

                private static IQuery<TaskSummary> TakeQuery(int take) =>
                    Sql.Table<TaskEntity>().Take(take).Select(t => new TaskSummary(t.Id, t.Title));

                private static IQuery<int> CountQuery() =>
                    Sql.Table<TaskEntity>().Skip(5).Take(10).Count();
            }
            """);

        Assert.Empty(result.Results[0].Diagnostics);
        Assert.Single(result.Results[0].GeneratedSources);
    }

    [Theory]
    // Terminals end the chain.
    [InlineData(".Where(t => t.IsActive).Count().Where(n => n > 1)", "IQuery<int>", "Task<int>", "Where")]
    // A key is a column.
    [InlineData(".OrderBy(t => min).Select(t => new TaskSummary(t.Id, t.Title))", "IQuery<TaskSummary>", "Task<List<TaskSummary>>", "min")]
    [InlineData(".OrderBy(t => t.Order + 1).Select(t => new TaskSummary(t.Id, t.Title))", "IQuery<TaskSummary>", "Task<List<TaskSummary>>", "t.Order + 1")]
    // A second OrderBy would reorder; ThenBy adds a key.
    [InlineData(".OrderBy(t => t.Order).OrderBy(t => t.Id).Select(t => new TaskSummary(t.Id, t.Title))", "IQuery<TaskSummary>", "Task<List<TaskSummary>>", "OrderBy")]
    // SQL applies WHERE and ORDER BY before LIMIT and OFFSET; written after them they would apply to the page.
    [InlineData(".OrderBy(t => t.Order).Take(5).Where(t => t.IsActive).Select(t => new TaskSummary(t.Id, t.Title))", "IQuery<TaskSummary>", "Task<List<TaskSummary>>", "Where")]
    [InlineData(".Skip(5).OrderBy(t => t.Order).Select(t => new TaskSummary(t.Id, t.Title))", "IQuery<TaskSummary>", "Task<List<TaskSummary>>", "OrderBy")]
    [InlineData(".OrderBy(t => t.Order).Take(5).Skip(min).Select(t => new TaskSummary(t.Id, t.Title))", "IQuery<TaskSummary>", "Task<List<TaskSummary>>", "Skip")]
    [InlineData(".OrderBy(t => t.Order).Take(5).Take(min).Select(t => new TaskSummary(t.Id, t.Title))", "IQuery<TaskSummary>", "Task<List<TaskSummary>>", "Take")]
    [InlineData(".OrderBy(t => t.Order).Skip(5).Skip(min).Select(t => new TaskSummary(t.Id, t.Title))", "IQuery<TaskSummary>", "Task<List<TaskSummary>>", "Skip")]
    // A count is a parameter or a non-negative int literal.
    [InlineData(".OrderBy(t => t.Order).Take(-1).Select(t => new TaskSummary(t.Id, t.Title))", "IQuery<TaskSummary>", "Task<List<TaskSummary>>", "-1")]
    [InlineData(".OrderBy(t => t.Order).Take(min * 2).Select(t => new TaskSummary(t.Id, t.Title))", "IQuery<TaskSummary>", "Task<List<TaskSummary>>", "min * 2")]
    // Keyset rows pair a column with a parameter or a literal, from two Sql.Row calls.
    [InlineData(".Where(t => Sql.Row(t.Order, t.Id) > Sql.Row(t.Order, id)).Select(t => new TaskSummary(t.Id, t.Title))", "IQuery<TaskSummary>", "Task<List<TaskSummary>>", "Sql.Row(t.Order, t.Id) > Sql.Row(t.Order, id)")]
    [InlineData(".Where(t => Sql.Row(min, t.Id) > Sql.Row(min, id)).Select(t => new TaskSummary(t.Id, t.Title))", "IQuery<TaskSummary>", "Task<List<TaskSummary>>", "Sql.Row(min, t.Id) > Sql.Row(min, id)")]
    [InlineData(".Where(t => Sql.Row(t.Note, t.Id) > Sql.Row<string?, Guid>(null, id)).Select(t => new TaskSummary(t.Id, t.Title))", "IQuery<TaskSummary>", "Task<List<TaskSummary>>", "Sql.Row(t.Note, t.Id) > Sql.Row<string?, Guid>(null, id)")]
    [InlineData(".Where(t => Sql.Row(t.Order, t.Id) > default(SqlRow<int, Guid>)).Select(t => new TaskSummary(t.Id, t.Title))", "IQuery<TaskSummary>", "Task<List<TaskSummary>>", "Sql.Row(t.Order, t.Id) > default(SqlRow<int, Guid>)")]
    [InlineData(".Where(t => Sql.Row(t.Origin, t.Id) > Sql.Row(Origin.Manual, id)).Select(t => new TaskSummary(t.Id, t.Title))", "IQuery<TaskSummary>", "Task<List<TaskSummary>>", "Sql.Row(t.Origin, t.Id) > Sql.Row(Origin.Manual, id)")]
    public void Unsupported_ordering_paging_or_terminal_reports_ZAO102(string chain, string queryType, string returnType, string located)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(LinqWhereTests.Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial
            """ + " " + returnType + """
             Find(int min, Guid id, CancellationToken ct);

                private static
            """ + " " + queryType + """
             FindQuery(int min, Guid id) =>
                    Sql.Table<TaskEntity>()
            """ + chain + """
            ;
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Same(tree, diagnostic.Location.SourceTree);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal(located, tree.ToString().Substring(span.Start, span.Length));
    }

    // The map stores Kind by name: a keyset over it would order the names. Reported when bound.
    [Fact]
    public void Keyset_over_an_EnumAsString_column_reports_ZAO102()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(LinqWhereTests.Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(Guid id, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(Guid id) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => Sql.Row(t.Kind, t.Id) > Sql.Row(WorkKind.Bug, id))
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("t.Kind", tree.ToString().Substring(span.Start, span.Length));
        Assert.Contains("stores enum member names", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Captured_take_reports_ZAO103()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(LinqWhereTests.Prelude + Repo + """
                private static readonly int PageSize = 10;

                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery() =>
                    Sql.Table<TaskEntity>().OrderBy(t => t.Order).Take(PageSize).Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO103", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("PageSize", tree.ToString().Substring(span.Start, span.Length));
    }

    // Count reads an int, LongCount a long and Any a bool, each as a Task or a ValueTask.
    [Theory]
    [InlineData("IQuery<int>", ".Count()", "Task<long>")]
    [InlineData("IQuery<int>", ".Count()", "Task<int?>")]
    [InlineData("IQuery<int>", ".Count()", "Task<List<int>>")]
    [InlineData("IQuery<int>", ".Count()", "IAsyncEnumerable<int>")]
    [InlineData("IQuery<long>", ".LongCount()", "Task<int>")]
    [InlineData("IQuery<bool>", ".Any()", "ValueTask<int>")]
    public void Terminal_with_another_return_type_reports_ZAO112(string queryType, string terminal, string returnType)
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
            """ + terminal + """
            ;
            }
            """);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO112", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal(returnType, tree.ToString().Substring(span.Start, span.Length));
    }

    private static void VerifySnapshot(
        GeneratorDriverRunResult result,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
        => GeneratorSnapshot.Verify(result, testFilePath, testMethod);

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
