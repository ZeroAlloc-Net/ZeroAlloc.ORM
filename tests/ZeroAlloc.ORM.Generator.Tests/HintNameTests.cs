using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests;

// #236 — two repository classes that share a simple name used to be given the same hint
// name ("{ContainingTypeName}.g.cs"), which Roslyn rejects with CS8785 and drops BOTH
// generated sources. These tests pin the fix: the hint name is built from the fully
// qualified type name (namespace + containing types), so same-named repositories in
// different scopes each get a unique file and generated code.
public class HintNameTests
{
    [Fact]
    public void Same_named_repositories_in_different_namespaces_both_generate_with_unique_hint_names()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace App.Orders
            {
                public sealed partial class Repository
                {
                    private readonly IAsyncDbConnection _connection;
                    public Repository(IAsyncDbConnection connection) => _connection = connection;

                    [Query("SELECT 1")]
                    public partial Task<int> GetOneAsync(CancellationToken ct);
                }
            }

            namespace App.Billing
            {
                public sealed partial class Repository
                {
                    private readonly IAsyncDbConnection _connection;
                    public Repository(IAsyncDbConnection connection) => _connection = connection;

                    [Query("SELECT 2")]
                    public partial Task<int> GetTwoAsync(CancellationToken ct);
                }
            }
            """;

        var result = GeneratorHarness.RunGenerator(source);

        AssertNoDuplicateHintNameDiagnostic(result);

        var hintNames = result.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.HintName)
            .ToArray();

        Assert.Equal(2, hintNames.Length);
        Assert.Equal(hintNames.Length, hintNames.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(hintNames, h => h.Contains("App.Orders", StringComparison.Ordinal));
        Assert.Contains(hintNames, h => h.Contains("App.Billing", StringComparison.Ordinal));

        GeneratorSnapshot.Verify(result);
    }

    [Fact]
    public void Same_named_repositories_nested_in_different_containing_types_get_unique_hint_names()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace App.Feature
            {
                public partial class OuterOne
                {
                    public sealed partial class Repository
                    {
                        private readonly IAsyncDbConnection _connection;
                        public Repository(IAsyncDbConnection connection) => _connection = connection;

                        [Query("SELECT 1")]
                        public partial Task<int> GetOneAsync(CancellationToken ct);
                    }
                }

                public partial class OuterTwo
                {
                    public sealed partial class Repository
                    {
                        private readonly IAsyncDbConnection _connection;
                        public Repository(IAsyncDbConnection connection) => _connection = connection;

                        [Query("SELECT 2")]
                        public partial Task<int> GetTwoAsync(CancellationToken ct);
                    }
                }
            }
            """;

        var result = GeneratorHarness.RunGenerator(source);

        AssertNoDuplicateHintNameDiagnostic(result);

        var hintNames = result.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.HintName)
            .ToArray();

        Assert.Equal(2, hintNames.Length);
        Assert.Equal(hintNames.Length, hintNames.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(hintNames, h => h.Contains("OuterOne", StringComparison.Ordinal));
        Assert.Contains(hintNames, h => h.Contains("OuterTwo", StringComparison.Ordinal));

        GeneratorSnapshot.Verify(result);
    }

    [Fact]
    public void Repository_in_the_global_namespace_still_generates_a_single_readable_hint_name()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            public sealed partial class GlobalRepository
            {
                private readonly IAsyncDbConnection _connection;
                public GlobalRepository(IAsyncDbConnection connection) => _connection = connection;

                [Query("SELECT 1")]
                public partial Task<int> GetOneAsync(CancellationToken ct);
            }
            """;

        var result = GeneratorHarness.RunGenerator(source);

        AssertNoDuplicateHintNameDiagnostic(result);

        var hintNames = result.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.HintName)
            .ToArray();

        Assert.Single(hintNames);
        Assert.Equal("GlobalRepository.g.cs", hintNames[0]);

        GeneratorSnapshot.Verify(result);
    }

    // Pulls every per-generator diagnostic into one LINQ query rather than a manual
    // foreach; a raw loop over `result.Results` (an ImmutableArray<GeneratorRunResult>)
    // boxes it on each iteration when handed to Assert.DoesNotContain (ZA0501).
    private static void AssertNoDuplicateHintNameDiagnostic(GeneratorDriverRunResult result)
    {
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "CS8785", StringComparison.Ordinal));

        var perGeneratorDiagnostics = result.Results.SelectMany(r => r.Diagnostics);
        Assert.DoesNotContain(perGeneratorDiagnostics, d => string.Equals(d.Id, "CS8785", StringComparison.Ordinal));
    }
}
