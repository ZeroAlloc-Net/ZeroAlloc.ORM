using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// #327: every ZeroAlloc consumer runs NetFabric.Hyperlinq.Analyzer with warnings as
// errors, and it analyzes generated code too. The emitted code must build there.
public class HyperlinqAnalyzerTests
{
    private const string Usings = """
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using ZeroAlloc.ORM;

        namespace TestApp;

        """;

    [Fact]
    public async Task Streaming_FlatRow_query_raises_no_Hyperlinq_diagnostic()
    {
        var source = Usings + """
            public sealed record OrderRow(int Id, int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, CustomerId, Total FROM Orders WHERE CustomerId = @customerId")]
                public partial IAsyncEnumerable<OrderRow> StreamAsync(int customerId, [EnumeratorCancellation] CancellationToken ct);
            }
            """;

        (await HyperlinqDiagnosticsInGeneratedCode(source)).Should().BeEmpty();
    }

    [Fact]
    public async Task Streaming_DomainEntity_query_raises_no_Hyperlinq_diagnostic()
    {
        var source = Usings + """
            public sealed class Customer
            {
                public Customer(int id, string name, string? nickname) { Id = id; Name = name; Nickname = nickname; }
                public int Id { get; }
                public string Name { get; }
                public string? Nickname { get; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Name, Nickname FROM Customers")]
                public partial IAsyncEnumerable<Customer> StreamAsync([EnumeratorCancellation] CancellationToken ct);
            }
            """;

        (await HyperlinqDiagnosticsInGeneratedCode(source)).Should().BeEmpty();
    }

    private static async Task<string[]> HyperlinqDiagnosticsInGeneratedCode(string source)
    {
        var (runResult, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source);
        compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the emitted code must compile before its analysis means anything");

        var analyzerPath = Path.Combine(AppContext.BaseDirectory, "Analyzers", "NetFabric.Hyperlinq.Analyzer.dll");
        File.Exists(analyzerPath).Should().BeTrue("the test project copies the analyzer next to the tests");
        var analyzers = new AnalyzerFileReference(analyzerPath, new AnalyzerAssemblyLoader())
            .GetAnalyzers(LanguageNames.CSharp);
        analyzers.Should().NotBeEmpty();

        var diagnostics = await compilation
            .WithAnalyzers(analyzers, new CompilationWithAnalyzersOptions(
                new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
                onAnalyzerException: null,
                concurrentAnalysis: false,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false))
            .GetAnalyzerDiagnosticsAsync().ConfigureAwait(false);

        var generatedTrees = runResult.GeneratedTrees.ToHashSet();
        generatedTrees.Should().NotBeEmpty();
        return diagnostics
            .Where(d => d.Location.SourceTree is { } tree && generatedTrees.Contains(tree))
            .Select(d => d.ToString())
            .ToArray();
    }

    private sealed class AnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public System.Reflection.Assembly LoadFromPath(string fullPath)
            => System.Reflection.Assembly.LoadFrom(fullPath);
    }
}
