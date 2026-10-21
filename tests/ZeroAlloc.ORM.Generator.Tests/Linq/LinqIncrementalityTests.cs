using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// The [LinqQuery] pipeline reads each method into a symbol-free model, then binds it to the
// entity maps. An edit that touches neither the method nor a map must leave both cached.
public class LinqIncrementalityTests
{
    private const string Store = """
        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class TaskStore(IAsyncDbConnection connection)
        {
            [LinqQuery(nameof(SummariesQuery))]
            public partial Task<List<TaskSummary>> Summaries(CancellationToken ct);

            private static IQuery<TaskSummary> SummariesQuery() =>
                Sql.Table<TaskEntity>().Select(t => new TaskSummary(t.Id, t.Title));
        }
        """;

    private static CSharpGeneratorDriver CreateDriver()
        => CSharpGeneratorDriver.Create(
            new[] { new ZeroAlloc.ORM.Generator.OrmGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static void AssertCached(GeneratorRunResult run, params string[] steps)
    {
        foreach (var name in steps)
        {
            Assert.True(run.TrackedSteps.ContainsKey(name), $"Step '{name}' did not run.");
            foreach (var step in run.TrackedSteps[name])
            {
                foreach (var (_, reason) in step.Outputs)
                {
                    Assert.True(
                        reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                        $"Step '{name}' output was {reason}.");
                }
            }
        }
    }

    // A filtered chain: predicates, a WhereIf, an enum bound by name and parameter facets all
    // live in the cached models, so they must compare equal across runs too.
    private const string FilteredStore = """
        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class TaskStore(IAsyncDbConnection connection)
        {
            [LinqQuery(nameof(SummariesQuery))]
            public partial Task<List<TaskSummary>> Summaries(WorkKind? kind, string title, CancellationToken ct);

            private static IQuery<TaskSummary> SummariesQuery(WorkKind? kind, string title) =>
                Sql.Table<TaskEntity>()
                    .WhereIf(kind != null, t => t.Kind == kind)
                    .Where(t => t.Title.Contains(title) && t.Status == WorkStatus.Done)
                    .Select(t => new TaskSummary(t.Id, t.Title));
        }
        """;

    // Ordering, keyset rows, paging and a terminal: the row comparison, the aggregate statement
    // and its paged source must compare equal across runs too.
    private const string PagedStore = """
        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class TaskStore(IAsyncDbConnection connection)
        {
            [LinqQuery(nameof(PageQuery))]
            public partial Task<List<TaskSummary>> Page(DateTimeOffset afterAt, Guid afterId, int take, CancellationToken ct);

            [LinqQuery(nameof(CountQuery))]
            public partial Task<int> CountPage(int skip, int take, CancellationToken ct);

            private static IQuery<TaskSummary> PageQuery(DateTimeOffset afterAt, Guid afterId, int take) =>
                Sql.Table<TaskEntity>()
                    .Where(t => Sql.Row(t.CreatedAt, t.Id) > Sql.Row(afterAt, afterId))
                    .OrderBy(t => t.CreatedAt)
                    .ThenByDescending(t => t.Id)
                    .Take(take)
                    .Select(t => new TaskSummary(t.Id, t.Title));

            private static IQuery<int> CountQuery(int skip, int take) =>
                Sql.Table<TaskEntity>().Where(t => t.Status == WorkStatus.Done).Skip(skip).Take(take).Count();
        }
        """;

    [Fact]
    public void Unrelated_edit_leaves_the_maps_and_the_linq_method_cached()
        => AssertUnrelatedEditCached(Store);

    [Fact]
    public void Unrelated_edit_leaves_a_filtered_linq_method_cached()
        => AssertUnrelatedEditCached(FilteredStore);

    [Fact]
    public void Unrelated_edit_leaves_a_paged_and_counted_linq_method_cached()
        => AssertUnrelatedEditCached(PagedStore, LinqWhereTests.Prelude);

    private static void AssertUnrelatedEditCached(string storeSource, string prelude = LinqProjectionTests.Prelude)
    {
        var store = CSharpSyntaxTree.ParseText(prelude + storeSource, path: "/src/Store.cs");
        var other = CSharpSyntaxTree.ParseText("namespace TestApp; public static class Other { }", path: "/src/Other.cs");
        var compilation = GeneratorHarness.CreateCompilation([store, other]);

        var driver = CreateDriver().RunGenerators(compilation);
        var first = driver.GetRunResult().Results[0];
        Assert.Empty(first.Diagnostics);

        var edited = compilation.ReplaceSyntaxTree(
            other,
            CSharpSyntaxTree.ParseText("namespace TestApp; public static class Other { public static int X => 1; }", path: "/src/Other.cs"));
        var second = driver.RunGenerators(edited).GetRunResult().Results[0];

        AssertCached(second, "EntityMaps", "LinqQueryReads", "LinqQueryMethods");
    }

    // ZAO109: invocations are read by name only, and only a call whose name a companion has is
    // bound. An unrelated edit leaves the companion set, the names and the bound call unchanged.
    [Fact]
    public void Unrelated_edit_leaves_the_companion_call_steps_cached()
    {
        var store = CSharpSyntaxTree.ParseText(
            LinqProjectionTests.Prelude + Store.Replace(
                "private static IQuery<TaskSummary> SummariesQuery()",
                "public IQuery<TaskSummary> Wrong() => SummariesQuery();\n\n    private static IQuery<TaskSummary> SummariesQuery()",
                StringComparison.Ordinal),
            path: "/src/Store.cs");
        var other = CSharpSyntaxTree.ParseText(
            "namespace TestApp; public static class Other { public static int Y() => System.Math.Abs(-1); }",
            path: "/src/Other.cs");
        var compilation = GeneratorHarness.CreateCompilation([store, other]);

        var driver = CreateDriver().RunGenerators(compilation);
        var first = driver.GetRunResult().Results[0];
        Assert.Equal("ZAO109", Assert.Single(first.Diagnostics).Id);

        var edited = compilation.ReplaceSyntaxTree(
            other,
            CSharpSyntaxTree.ParseText(
                "namespace TestApp; public static class Other { public static int Y() => System.Math.Abs(-1); public static int X => 1; }",
                path: "/src/Other.cs"));
        var second = driver.RunGenerators(edited).GetRunResult().Results[0];

        Assert.Equal("ZAO109", Assert.Single(second.Diagnostics).Id);
        AssertCached(second, "LinqCompanions", "CompanionCalls");
        foreach (var step in second.TrackedSteps["InvocationNames"])
        {
            foreach (var (value, reason) in step.Outputs)
            {
                // Only the edited file's invocations are read again.
                var call = (ZeroAlloc.ORM.Generator.Linq.InvocationCandidate)value;
                if (string.Equals(call.Location.Tree.FilePath, "/src/Other.cs", StringComparison.Ordinal)) continue;
                Assert.True(
                    reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"InvocationNames output '{call.Name}' was {reason}.");
            }
        }
    }

    [Fact]
    public void Editing_the_map_rebinds_the_method_to_the_new_table()
    {
        var store = CSharpSyntaxTree.ParseText(LinqProjectionTests.Prelude + Store, path: "/src/Store.cs");
        var compilation = GeneratorHarness.CreateCompilation([store]);
        var driver = CreateDriver().RunGenerators(compilation);

        var renamed = compilation.ReplaceSyntaxTree(
            store,
            CSharpSyntaxTree.ParseText(
                (LinqProjectionTests.Prelude + Store).Replace("\"Tasks\"", "\"Work\"", StringComparison.Ordinal),
                path: "/src/Store.cs"));
        driver = driver.RunGenerators(renamed);

        var source = Assert.Single(driver.GetRunResult().Results[0].GeneratedSources).SourceText.ToString();
        Assert.Contains("FROM \\\"Work\\\"", source, StringComparison.Ordinal);
    }
}
