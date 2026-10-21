using System;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// The LINQ front end's errors: each case reports exactly one generator diagnostic, at the
// node that causes it, and the method gets no implementation.
public class LinqFrontEndDiagnosticsTests
{
    private const string Repo = """
        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class TaskStore(IAsyncDbConnection connection)
        {

        """;

    // Runs the generator and returns its single diagnostic plus the source text it points at.
    private static (Diagnostic Diagnostic, string Located) Single(string body)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(LinqProjectionTests.Prelude + body);
        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Same(tree, diagnostic.Location.SourceTree);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.Results[0].GeneratedSources);
        var span = diagnostic.Location.SourceSpan;
        return (diagnostic, tree.ToString().Substring(span.Start, span.Length));
    }

    private static string Message(Diagnostic d) => d.GetMessage(CultureInfo.InvariantCulture);

    [Fact]
    public void Missing_companion_reports_ZAO100_at_the_attribute_argument()
    {
        var (d, located) = Single(Repo + """
                [LinqQuery("Nope")]
                public partial Task<List<TaskSummary>> Summaries(CancellationToken ct);
            }
            """);

        Assert.Equal("ZAO100", d.Id);
        Assert.Equal("\"Nope\"", located);
        Assert.Contains("not found", Message(d), StringComparison.Ordinal);
    }

    [Fact]
    public void Instance_companion_reports_ZAO100_at_the_companion()
    {
        var (d, located) = Single(Repo + """
                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(CancellationToken ct);

                private IQuery<TaskSummary> SummariesQuery() =>
                    Sql.Table<TaskEntity>().Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        Assert.Equal("ZAO100", d.Id);
        Assert.Equal("SummariesQuery", located);
        Assert.Contains("static", Message(d), StringComparison.Ordinal);
    }

    [Fact]
    public void Companion_built_through_locals_reports_ZAO100()
    {
        var (d, located) = Single(Repo + """
                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(CancellationToken ct);

                private static IQuery<TaskSummary> SummariesQuery()
                {
                    var q = Sql.Table<TaskEntity>();
                    return q.Select(t => new TaskSummary(t.Id, t.Title));
                }
            }
            """);

        Assert.Equal("ZAO100", d.Id);
        Assert.Equal("SummariesQuery", located);
    }

    [Fact]
    public void Companion_not_returning_IQuery_reports_ZAO100_at_its_return_type()
    {
        var (d, located) = Single(Repo + """
                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(CancellationToken ct);

                private static IOrderedQuery<TaskSummary> SummariesQuery() => throw new NotSupportedException();
            }
            """);

        Assert.Equal("ZAO100", d.Id);
        Assert.Equal("IOrderedQuery<TaskSummary>", located);
    }

    [Fact]
    public void Mismatched_companion_parameters_report_ZAO101()
    {
        var (d, located) = Single(Repo + """
                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(Guid id, int rank, CancellationToken ct);

                private static IQuery<TaskSummary> SummariesQuery(int rank, Guid id) =>
                    Sql.Table<TaskEntity>().Select(t => new TaskSummary(id, t.Title));
            }
            """);

        Assert.Equal("ZAO101", d.Id);
        Assert.Equal("(int rank, Guid id)", located);
    }

    [Fact]
    public void Unsupported_operator_reports_ZAO102_at_the_operator()
    {
        var (d, located) = Single("""
            public static class MyOperators
            {
                public static IQuery<T> Shuffle<T>(this IQuery<T> source) => source;
            }

            """ + Repo + """
                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(CancellationToken ct);

                private static IQuery<TaskSummary> SummariesQuery() =>
                    Sql.Table<TaskEntity>().Shuffle().Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        Assert.Equal("ZAO102", d.Id);
        Assert.Equal("Shuffle", located);
        Assert.Contains("Shuffle", Message(d), StringComparison.Ordinal);
    }

    [Fact]
    public void Captured_field_reports_ZAO103_at_the_reference()
    {
        var (d, located) = Single(Repo + """
                private static readonly string Label = "x";

                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(CancellationToken ct);

                private static IQuery<TaskSummary> SummariesQuery() =>
                    Sql.Table<TaskEntity>().Select(t => new TaskSummary(t.Id, Label));
            }
            """);

        Assert.Equal("ZAO103", d.Id);
        Assert.Equal("Label", located);
    }

    [Fact]
    public void Static_field_chain_source_reports_ZAO103()
    {
        var (d, located) = Single(Repo + """
                private static readonly IQuery<TaskEntity> AllTasks = Sql.Table<TaskEntity>();

                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(CancellationToken ct);

                private static IQuery<TaskSummary> SummariesQuery() =>
                    AllTasks.Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        Assert.Equal("ZAO103", d.Id);
        Assert.Equal("AllTasks", located);
    }

    [Fact]
    public void Parameter_chain_source_reports_ZAO102()
    {
        var (d, located) = Single(Repo + """
                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(IQuery<TaskEntity> source, CancellationToken ct);

                private static IQuery<TaskSummary> SummariesQuery(IQuery<TaskEntity> source) =>
                    source.Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        Assert.Equal("ZAO102", d.Id);
        Assert.Equal("source", located);
        Assert.Contains("chain source", Message(d), StringComparison.Ordinal);
    }

    [Fact]
    public void Unmapped_entity_reports_ZAO104_at_the_table_call()
    {
        var (d, located) = Single("""
            public sealed class Ghost { public Guid Id { get; private set; } public string Title { get; private set; } = ""; }

            """ + Repo + """
                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(CancellationToken ct);

                private static IQuery<TaskSummary> SummariesQuery() =>
                    Sql.Table<Ghost>().Select(g => new TaskSummary(g.Id, g.Title));
            }
            """);

        Assert.Equal("ZAO104", d.Id);
        Assert.Equal("Sql.Table<Ghost>()", located);
        Assert.Contains("Ghost", Message(d), StringComparison.Ordinal);
    }

    [Fact]
    public void Unmapped_member_reports_ZAO105_at_the_member()
    {
        var (d, located) = Single("""
            public sealed class Note
            {
                public Guid Id { get; private set; }
                internal string Secret { get; private set; } = "";
            }

            [TableMap(typeof(Note), "Notes")]
            internal static class NoteMap;

            """ + Repo + """
                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(CancellationToken ct);

                private static IQuery<TaskSummary> SummariesQuery() =>
                    Sql.Table<Note>().Select(n => new TaskSummary(n.Id, n.Secret));
            }
            """);

        Assert.Equal("ZAO105", d.Id);
        Assert.Equal("n.Secret", located);
        Assert.Contains("Secret", Message(d), StringComparison.Ordinal);
    }

    // A computed property has no column behind it, so it is not mapped.
    [Fact]
    public void Computed_property_reports_ZAO105_at_the_member()
    {
        var (d, located) = Single("""
            public sealed class Note
            {
                public Guid Id { get; private set; }
                public string Title { get; private set; } = "";
                public string Label => Title + "!";
            }

            [TableMap(typeof(Note), "Notes")]
            internal static class NoteMap;

            """ + Repo + """
                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(CancellationToken ct);

                private static IQuery<TaskSummary> SummariesQuery() =>
                    Sql.Table<Note>().Select(n => new TaskSummary(n.Id, n.Label));
            }
            """);

        Assert.Equal("ZAO105", d.Id);
        Assert.Equal("n.Label", located);
    }

    [Fact]
    public void Scalar_projection_reports_ZAO107_at_the_projection()
    {
        var (d, located) = Single(Repo + """
                [LinqQuery(nameof(TitlesQuery))]
                public partial Task<List<string>> Titles(CancellationToken ct);

                private static IQuery<string> TitlesQuery() =>
                    Sql.Table<TaskEntity>().Select(t => t.Title);
            }
            """);

        Assert.Equal("ZAO107", d.Id);
        Assert.Equal("t.Title", located);
    }

    private const string Priced = """
        public sealed record Money(decimal Amount, string Currency);

        public readonly record struct Span(int From, int To);

        [Materialize(Factory = nameof(FromStorage))]
        public sealed class Period
        {
            private Period(int days) => Days = days;

            public int Days { get; }

            public static Period FromStorage(int days, string unit) => new(days);
        }

        public sealed class Priced
        {
            public Guid Id { get; private set; }
            public Money Price { get; private set; } = new(0m, "EUR");
            public Span Window { get; private set; }
            public Period? Term { get; private set; }
        }

        [TableMap(typeof(Priced), "Priced")]
        internal static class PricedMap;

        public sealed record PricedRow(Guid Id, Money Price);

        public sealed record PricedLine(Guid Id);

        """;

    // A row member whose type reads several columns gets one column from the projection.
    [Fact]
    public void Row_member_reading_several_columns_reports_ZAO107_at_the_return_type()
    {
        var (d, located) = Single(Priced + Repo + """
                [LinqQuery(nameof(PricesQuery))]
                public partial Task<List<PricedRow>> Prices(CancellationToken ct);

                private static IQuery<PricedRow> PricesQuery() =>
                    Sql.Table<Priced>().Select(p => new PricedRow(p.Id, null!));
            }
            """);

        Assert.Equal("ZAO107", d.Id);
        Assert.Equal("Task<List<PricedRow>>", located);
        Assert.Contains("Price", Message(d), StringComparison.Ordinal);
    }

    // A composite member maps to several columns and a composite parameter binds as several
    // values, @total_Amount and @total_Currency, while the SQL would name one: ZAO102, before
    // the query could fail at run time.
    [Theory]
    [InlineData("p => p.Price == null", "int order", "p.Price")]
    [InlineData("p => p.Window == window", "Span window", "Span window")]
    [InlineData("p => p.Price == total", "Money total", "Money total")]
    [InlineData("p => total == null", "Money? total", "Money? total")]
    [InlineData("p => window == null", "Span? window", "Span? window")]
    [InlineData("p => p.Term == null", "int order", "p.Term")]
    [InlineData("p => term == null", "Period? term", "Period? term")]
    public void Composite_member_or_parameter_reports_ZAO102(string predicate, string parameter, string expected)
    {
        var (d, located) = Single(Priced + Repo + """
                [LinqQuery(nameof(PricesQuery))]
                public partial Task<List<PricedLine>> Prices(
            """ + parameter + """
            , CancellationToken ct);

                private static IQuery<PricedLine> PricesQuery(
            """ + parameter + """
            ) =>
                    Sql.Table<Priced>().Where(
            """ + predicate + """
            ).Select(p => new PricedLine(p.Id));
            }
            """);

        Assert.Equal("ZAO102", d.Id);
        Assert.Equal(expected, located);
        Assert.Contains("several", Message(d), StringComparison.Ordinal);
    }

    [Fact]
    public void Composite_member_in_a_projection_reports_ZAO102_at_the_member()
    {
        var (d, located) = Single(Priced + Repo + """
                [LinqQuery(nameof(PricesQuery))]
                public partial Task<List<PricedRow>> Prices(CancellationToken ct);

                private static IQuery<PricedRow> PricesQuery() =>
                    Sql.Table<Priced>().Select(p => new PricedRow(p.Id, p.Price));
            }
            """);

        Assert.Equal("ZAO102", d.Id);
        Assert.Equal("p.Price", located);
    }

    [Fact]
    public void Missing_dialect_reports_ZAO108_at_the_method()
    {
        var (d, located) = Single("""
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(CancellationToken ct);

                private static IQuery<TaskSummary> SummariesQuery() =>
                    Sql.Table<TaskEntity>().Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        Assert.Equal("ZAO108", d.Id);
        Assert.Equal("Summaries", located);
    }

    [Fact]
    public void Non_postgres_dialect_reports_ZAO108()
    {
        var (d, _) = Single("""
            [Dialect(SqlDialect.SqlServer)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(CancellationToken ct);

                private static IQuery<TaskSummary> SummariesQuery() =>
                    Sql.Table<TaskEntity>().Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        Assert.Equal("ZAO108", d.Id);
        Assert.Contains("SqlServer", Message(d), StringComparison.Ordinal);
    }

    [Fact]
    public void Non_nullable_single_row_reports_ZAO112_at_the_return_type()
    {
        var (d, located) = Single(Repo + """
                [LinqQuery(nameof(FirstQuery))]
                public partial Task<TaskSummary> First(CancellationToken ct);

                private static IQuery<TaskSummary> FirstQuery() =>
                    Sql.Table<TaskEntity>().Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        Assert.Equal("ZAO112", d.Id);
        Assert.Equal("Task<TaskSummary>", located);
    }

    [Fact]
    public void Other_row_type_reports_ZAO112()
    {
        var (d, located) = Single(Repo + """
                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskEntity>> Summaries(CancellationToken ct);

                private static IQuery<TaskSummary> SummariesQuery() =>
                    Sql.Table<TaskEntity>().Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);

        Assert.Equal("ZAO112", d.Id);
        Assert.Equal("Task<List<TaskEntity>>", located);
    }
}
