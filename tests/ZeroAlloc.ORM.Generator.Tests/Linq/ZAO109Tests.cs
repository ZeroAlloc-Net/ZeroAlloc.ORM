using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using ZeroAlloc.ORM.Generator.Linq;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// ZAO109: a [LinqQuery] companion is read at build time and never runs, so calling it, or
// taking it as a method group, is an error. LinqCompanionCallAnalyzer reports it; only a call
// that binds to a companion does, and a method of the same name in another type does not.
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

    private const string Message = "'SummariesQuery' is a [LinqQuery] companion and is never run; call 'Summaries' instead";

    // Runs the generator, as a build does, then the analyzer over the result. Returns the
    // analyzer's diagnostics in source order, each with the text it points at, and the
    // generator's own diagnostics.
    private static async Task<((Diagnostic Diagnostic, string Located)[] Reported, ImmutableArray<Diagnostic> Generator)> AnalyzeAsync(string source)
    {
        var (run, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(LinqProjectionTests.Prelude + source);
        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new LinqCompanionCallAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);
        var reported = diagnostics
            .OrderBy(d => d.Location.SourceSpan.Start)
            .Select(d => (d, d.Location.SourceTree!.ToString().Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)))
            .ToArray();
        return (reported, run.Results[0].Diagnostics);
    }

    [Fact]
    public async Task Calling_a_companion_reports_ZAO109()
    {
        var (reported, generator) = await AnalyzeAsync(Store + """
            public static class Caller
            {
                public static IQuery<TaskSummary> Outside() => TaskStore.SummariesQuery(2);
            }
            """);

        Assert.Equal(["SummariesQuery(1)", "TaskStore.SummariesQuery(2)"], reported.Select(r => r.Located).ToArray());
        foreach (var (d, _) in reported)
        {
            Assert.Equal("ZAO109", d.Id);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Equal(Message, d.GetMessage(CultureInfo.InvariantCulture));
        }

        // The generator no longer reports it, and nameof(SummariesQuery) in the attribute is no call.
        Assert.Empty(generator);
    }

    [Fact]
    public async Task Method_group_reports_ZAO109()
    {
        var (reported, _) = await AnalyzeAsync(Store + """
            public static class Caller
            {
                public static Func<int, IQuery<TaskSummary>> Group() => TaskStore.SummariesQuery;
            }
            """);

        Assert.Equal(["SummariesQuery(1)", "TaskStore.SummariesQuery"], reported.Select(r => r.Located).ToArray());
        Assert.All(reported, r => Assert.Equal("ZAO109", r.Diagnostic.Id));
    }

    // A static companion is reachable through a derived type too; it is still the base's.
    [Fact]
    public async Task Call_through_a_derived_class_reports_ZAO109()
    {
        var (reported, _) = await AnalyzeAsync("""
            [Dialect(SqlDialect.PostgreSql)]
            public partial class BaseStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(int min, CancellationToken ct);

                protected internal static IQuery<TaskSummary> SummariesQuery(int min) =>
                    Sql.Table<TaskEntity>().Where(t => t.Order >= min).Select(t => new TaskSummary(t.Id, t.Title));
            }

            public sealed class DerivedStore(IAsyncDbConnection connection) : BaseStore(connection)
            {
                public static IQuery<TaskSummary> Inherited() => SummariesQuery(5);
            }

            public static class Caller
            {
                public static IQuery<TaskSummary> Outside() => DerivedStore.SummariesQuery(6);
            }
            """);

        Assert.Equal(["SummariesQuery(5)", "DerivedStore.SummariesQuery(6)"], reported.Select(r => r.Located).ToArray());
        Assert.All(reported, r => Assert.Equal(Message, r.Diagnostic.GetMessage(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Call_on_a_constructed_generic_type_reports_ZAO109()
    {
        var (reported, _) = await AnalyzeAsync("""
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class GenericStore<TMarker>(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(SummariesQuery))]
                public partial Task<List<TaskSummary>> Summaries(int min, CancellationToken ct);

                internal static IQuery<TaskSummary> SummariesQuery(int min) =>
                    Sql.Table<TaskEntity>().Where(t => t.Order >= min).Select(t => new TaskSummary(t.Id, t.Title));
            }

            public static class Caller
            {
                public static IQuery<TaskSummary> Outside() => GenericStore<int>.SummariesQuery(7);
            }
            """);

        Assert.Equal("GenericStore<int>.SummariesQuery(7)", Assert.Single(reported).Located);
    }

    [Fact]
    public async Task Same_named_method_in_another_type_does_not_report_ZAO109()
    {
        var (reported, generator) = await AnalyzeAsync(Store.Replace(
            "public IQuery<TaskSummary> Inside() => SummariesQuery(1);",
            "public static int Inside() => Other.SummariesQuery(1);",
            StringComparison.Ordinal) + """
            public static class Other
            {
                public static int SummariesQuery(int min) => min;

                public static int Caller() => SummariesQuery(3) + Other.SummariesQuery(4);

                public static Func<int, int> Group() => SummariesQuery;
            }
            """);

        Assert.Empty(reported);
        Assert.Empty(generator);
    }
}
