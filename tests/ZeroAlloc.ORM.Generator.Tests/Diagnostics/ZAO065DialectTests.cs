using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// #307 — ZAO065 warns on a decimal output without a scale because SqlClient rounds it to scale 0.
// Another provider returns the value exact, so the warning is skipped when the repository
// declares a dialect other than SqlServer. Without a declared dialect it is reported as before.
public class ZAO065DialectTests
{
    private const string Members = """
            [StoredProcedure("usp_Quote")]
            public partial Task<(int Id, decimal Total)> QuoteAsync(int id, decimal total, CancellationToken ct);
        """;

    private static GeneratorDriverRunResult Run(string repositoryAttribute, string assemblyAttribute = "")
        => GeneratorHarness.RunGenerator($$"""
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;
            {{assemblyAttribute}}
            namespace TestApp
            {
                {{repositoryAttribute}}
                public sealed partial class Repo(IAsyncDbConnection connection)
                {
            {{Members}}
                }
            }
            """);

    private static int Zao065Count(GeneratorDriverRunResult result)
        => result.Diagnostics.Count(d => string.Equals(d.Id, "ZAO065", StringComparison.Ordinal));

    [Theory]
    [InlineData("")]
    [InlineData("[Dialect(SqlDialect.SqlServer)]")]
    [InlineData("[Dialect((SqlDialect)42)]")]
    public void Reported_without_a_dialect_and_on_SqlServer(string repositoryAttribute)
    {
        var result = Run(repositoryAttribute);

        Assert.Equal(1, Zao065Count(result));
        // A warning: the repository is still emitted.
        Assert.Single(result.GeneratedTrees);
    }

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    public void Skipped_on_a_declared_dialect_other_than_SqlServer(string dialect)
    {
        var result = Run($"[Dialect(SqlDialect.{dialect})]");

        Assert.Equal(0, Zao065Count(result));
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
        Assert.Single(result.GeneratedTrees);
    }

    [Fact]
    public void Skipped_through_the_assembly_default()
        => Assert.Equal(0, Zao065Count(Run(string.Empty, "[assembly: Dialect(SqlDialect.PostgreSql)]")));

    [Fact]
    public void Repository_attribute_overrides_the_assembly_default()
    {
        Assert.Equal(1, Zao065Count(Run(
            "[Dialect(SqlDialect.SqlServer)]", "[assembly: Dialect(SqlDialect.PostgreSql)]")));
        Assert.Equal(0, Zao065Count(Run(
            "[Dialect(SqlDialect.PostgreSql)]", "[assembly: Dialect(SqlDialect.SqlServer)]")));
    }
}
