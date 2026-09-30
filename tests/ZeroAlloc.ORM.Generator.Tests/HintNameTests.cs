using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests;

// #236 — two repository classes that share a simple name used to be given the same hint
// name ("{ContainingTypeName}.g.cs"), which Roslyn rejects with CS8785 and drops ALL
// generated sources. #303 — the qualified name that fixed it still collided, because it
// spelled generic outer types with their type-parameter names and escaped the brackets.
// These tests pin the scheme HintNames.ForRepository uses: namespace, then containing types
// and the repository joined by `+`, each with its arity.
public class HintNameTests
{
    private static readonly string[] NamespaceHintNames =
    [
        "App.Billing.Repository.g.cs",
        "App.Orders.Repository.g.cs",
    ];

    // Nesting is written with `+`, so a nested repository is never named like a repository at
    // the top of a namespace with its outer type's name.
    private static readonly string[] NestedHintNames =
    [
        "App.Feature.OuterOne+Repository.g.cs",
        "App.Feature.OuterTwo+Repository.g.cs",
    ];

    // Arity is written as `N, never with type-parameter names.
    private static readonly string[] ArityHintNames =
    [
        "App.Feature.Outer+Repository.g.cs",
        "App.Feature.Outer`1+Repository.g.cs",
        "App.Feature.Outer`2+Repository.g.cs",
    ];

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
        Assert.Equal(NamespaceHintNames, hintNames.OrderBy(h => h, StringComparer.Ordinal), StringComparer.Ordinal);

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
        Assert.Equal(NestedHintNames, hintNames.OrderBy(h => h, StringComparer.Ordinal), StringComparer.Ordinal);

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

    // Generic containing types are part of the hint name, written with their arity, so each
    // same-named outer type of a different arity gets its own file.
    [Fact]
    public void Repositories_nested_in_same_named_outer_types_of_different_arity_get_unique_hint_names()
    {
        var source = NestedRepositories("Outer", "Outer<T>", "Outer<T, U>");

        var result = GeneratorHarness.RunGenerator(source);

        AssertNoDuplicateHintNameDiagnostic(result);
        Assert.Equal(ArityHintNames, HintNames(result).OrderBy(h => h, StringComparer.Ordinal), StringComparer.Ordinal);

        GeneratorSnapshot.Verify(result);
    }

    // The hint name used to spell a generic outer type with its type-parameter names and turn
    // the brackets into underscores, so `Outer<T>` and a plain class named `Outer_T_` got the
    // same file name, and CS8785 dropped all generated output.
    [Fact]
    public void Generic_outer_type_and_a_type_named_like_its_old_escaped_form_get_unique_hint_names()
    {
        var source = NestedRepositories("Outer<T>", "Outer_T_");

        var result = GeneratorHarness.RunGenerator(source);

        AssertNoDuplicateHintNameDiagnostic(result);
        var hintNames = HintNames(result);
        Assert.Equal(2, hintNames.Length);
        Assert.Equal(hintNames.Length, hintNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // One `Repository` nested in each of the given outer type declarations, all in namespace
    // App.Feature; each repository gets its own query method so the generated files differ.
    private static string NestedRepositories(params string[] outerTypes)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("using System.Data.Async;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using ZeroAlloc.ORM;");
        sb.AppendLine();
        sb.AppendLine("namespace App.Feature");
        sb.AppendLine("{");
        for (var i = 0; i < outerTypes.Length; i++)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"    public partial class {outerTypes[i]}");
            sb.AppendLine("    {");
            sb.AppendLine("        public sealed partial class Repository");
            sb.AppendLine("        {");
            sb.AppendLine("            private readonly IAsyncDbConnection _connection;");
            sb.AppendLine("            public Repository(IAsyncDbConnection connection) => _connection = connection;");
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"            [Query(\"SELECT {i}\")]");
            sb.AppendLine(CultureInfo.InvariantCulture, $"            public partial Task<int> Get{i}Async(CancellationToken ct);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string[] HintNames(GeneratorDriverRunResult result) => result.Results
        .SelectMany(r => r.GeneratedSources)
        .Select(s => s.HintName)
        .ToArray();

    /// <summary>
    /// A C# identifier cannot hold a character that is invalid in a hint name, so escaping
    /// guards names the generator is handed rather than names users write. The escape starts
    /// with '-', which no identifier contains, so an escaped name cannot collide with one that
    /// needed none.
    /// </summary>
    [Theory]
    [InlineData("M", "M")]
    [InlineData("App.M", "App.M")]
    [InlineData("App.Outer+M`1", "App.Outer+M`1")]
    [InlineData("Café.Ωmega_1", "Café.Ωmega_1")]
    [InlineData("a/b|c:d*e?f<g>h", "a-u002Fb-u007Cc-u003Ad-u002Ae-u003Ff-u003Cg-u003Eh")]
    [InlineData("a-b", "a-u002Db")]
    public void Sanitize_keeps_identifier_characters_and_escapes_the_rest(string name, string expected)
    {
        Assert.Equal(expected, ZeroAlloc.ORM.Generator.HintNames.Sanitize(name));
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
