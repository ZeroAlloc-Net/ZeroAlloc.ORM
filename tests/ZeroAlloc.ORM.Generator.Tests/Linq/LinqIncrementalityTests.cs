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

    [Fact]
    public void Unrelated_edit_leaves_the_maps_and_the_linq_method_cached()
    {
        var store = CSharpSyntaxTree.ParseText(LinqProjectionTests.Prelude + Store, path: "/src/Store.cs");
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
