using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests;

// The cached models carry diagnostic locations bound to their syntax tree. An edit to another
// file keeps that tree instance, so every tracked step and output must stay cached; an edit to
// the repository's own file must move its diagnostics into the new tree.
public class IncrementalityTests
{
    private const string RepoSource = """
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT 1")]
            public partial async Task<int> GetOneAsync(CancellationToken ct);

            [Command("UPDATE X SET A = 1", Kind = CommandKind.NonQuery)]
            public partial Task<int> UpdateAsync(CancellationToken ct);

            [StoredProcedure("usp_X")]
            public partial Task<int> RunAsync(CancellationToken ct);
        }

        [StoreAsString]
        public sealed class NotAnEnum { }
        """;

    // The tracking names OrmGenerator gives its steps.
    private static readonly string[] TrackedStepNames =
    [
        "QueryMethods", "CommandMethods", "StoredProcedureMethods", "AllMethods", "Repositories",
        "StoreAsStringDiagnostics",
    ];

    [Fact]
    public void Unrelated_edit_leaves_every_tracked_step_and_output_cached()
    {
        var repo = CSharpSyntaxTree.ParseText(RepoSource, path: "/src/Repo.cs");
        var unrelated = CSharpSyntaxTree.ParseText(
            "namespace TestApp; public class Unrelated { public int M() => 1; }", path: "/src/Unrelated.cs");
        var compilation = GeneratorHarness.CreateCompilation(new[] { repo, unrelated });

        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGenerators(compilation);
        var first = driver.GetRunResult().Results[0];

        var edited = compilation.ReplaceSyntaxTree(
            unrelated,
            unrelated.WithChangedText(SourceText.From(
                "namespace TestApp; public class Unrelated { public int M() => 2; public int N() => 3; }")));
        driver = driver.RunGenerators(edited);
        var second = driver.GetRunResult().Results[0];

        foreach (var name in TrackedStepNames)
        {
            Assert.True(second.TrackedSteps.TryGetValue(name, out var runSteps), $"Step '{name}' was not tracked.");
            AssertAllCachedOrUnchanged(name, runSteps);
        }

        Assert.NotEmpty(second.TrackedOutputSteps);
        foreach (var step in second.TrackedOutputSteps)
            AssertAllCachedOrUnchanged(step.Key, step.Value);

        // A cached output still reports its diagnostics, in the same tree and at the same place.
        Assert.Contains(second.Diagnostics, d => string.Equals(d.Id, "ZAO009", StringComparison.Ordinal));
        Assert.Contains(second.Diagnostics, d => string.Equals(d.Id, "ZAO042", StringComparison.Ordinal));
        Assert.Equal(Describe(first.Diagnostics), Describe(second.Diagnostics));
        Assert.All(second.Diagnostics, d => Assert.Same(repo, d.Location.SourceTree));
    }

    [Fact]
    public void Edit_above_the_repository_moves_its_diagnostics_into_the_new_tree()
    {
        var repo = CSharpSyntaxTree.ParseText(RepoSource, path: "/src/Repo.cs");
        var compilation = GeneratorHarness.CreateCompilation(new[] { repo });

        GeneratorDriver driver = CreateDriver();
        driver = driver.RunGenerators(compilation);
        var before = Assert.Single(
            driver.GetRunResult().Diagnostics, d => string.Equals(d.Id, "ZAO009", StringComparison.Ordinal));

        var moved = repo.WithChangedText(SourceText.From(
            RepoSource.Replace("namespace TestApp;", "namespace TestApp;\n\n// two\n// more lines", StringComparison.Ordinal)));
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(repo, moved));
        var after = Assert.Single(
            driver.GetRunResult().Diagnostics, d => string.Equals(d.Id, "ZAO009", StringComparison.Ordinal));

        Assert.Same(moved, after.Location.SourceTree);
        Assert.Equal(
            before.Location.GetLineSpan().StartLinePosition.Line + 3,
            after.Location.GetLineSpan().StartLinePosition.Line);
    }

    private static CSharpGeneratorDriver CreateDriver()
        => CSharpGeneratorDriver.Create(
            new[] { new ZeroAlloc.ORM.Generator.OrmGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static void AssertAllCachedOrUnchanged(string stepName, ImmutableArray<IncrementalGeneratorRunStep> runSteps)
    {
        foreach (var runStep in runSteps)
        {
            foreach (var (_, reason) in runStep.Outputs)
            {
                Assert.True(
                    reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"Step '{stepName}' output was {reason}, expected Cached or Unchanged.");
            }
        }
    }

    private static string[] Describe(ImmutableArray<Diagnostic> diagnostics)
        => diagnostics
            .Select(d => d.Id + "@" + d.Location.GetLineSpan())
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
}
