using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// ZAO109: a [LinqQuery] companion is read at build time and never runs, so calling it is an
// error. Only a call that binds to a companion reports it; a method of the same name in
// another type does not.
public class ZAO109Tests
{
    private const string Store = """
        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class TaskStore(IAsyncDbConnection connection)
        {
            [LinqQuery(nameof(SummariesQuery))]
            public partial Task<List<TaskSummary>> Summaries(int min, CancellationToken ct);

            internal static IQuery<TaskSummary> SummariesQuery(int min) =>
                Sql.Table<TaskEntity>().Where(t => t.Order >= min).Select(t => new TaskSummary(t.Id, t.Title));

            public IQuery<TaskSummary> Inside() => SummariesQuery(1);
        }

        """;

    [Fact]
    public void Calling_a_companion_reports_ZAO109()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(LinqProjectionTests.Prelude + Store + """
            public static class Caller
            {
                public static IQuery<TaskSummary> Outside() => TaskStore.SummariesQuery(2);
            }
            """);

        var run = result.Results[0];
        var diagnostics = run.Diagnostics.OrderBy(d => d.Location.SourceSpan.Start).ToArray();
        Assert.Equal(2, diagnostics.Length);
        var text = tree.ToString();
        string Located(Diagnostic d) => text.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length);

        foreach (var d in diagnostics)
        {
            Assert.Equal("ZAO109", d.Id);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Same(tree, d.Location.SourceTree);
            Assert.Equal(
                "'SummariesQuery' is a [LinqQuery] companion and is never run; call 'Summaries' instead",
                d.GetMessage(CultureInfo.InvariantCulture));
        }
        Assert.Equal("SummariesQuery(1)", Located(diagnostics[0]));
        Assert.Equal("TaskStore.SummariesQuery(2)", Located(diagnostics[1]));

        // The diagnostic is about the call site; the [LinqQuery] method is still generated.
        Assert.Single(run.GeneratedSources);
    }

    [Fact]
    public void Same_named_method_in_another_type_does_not_report_ZAO109()
    {
        var result = GeneratorHarness.RunGenerator(LinqProjectionTests.Prelude + Store.Replace(
            "public IQuery<TaskSummary> Inside() => SummariesQuery(1);",
            "public static int Inside() => Other.SummariesQuery(1);",
            StringComparison.Ordinal) + """
            public static class Other
            {
                public static int SummariesQuery(int min) => min;

                public static int Caller() => SummariesQuery(3) + Other.SummariesQuery(4);
            }
            """);

        var run = result.Results[0];
        Assert.Empty(run.Diagnostics);
        Assert.Single(run.GeneratedSources);
    }
}
