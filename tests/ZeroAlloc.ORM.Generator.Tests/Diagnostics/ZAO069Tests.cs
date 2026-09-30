using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// #307 — ZAO069 reports a parameter bound as the procedure's RETURN value, explicitly or by the
// RETURN_VALUE convention, when the repository declares a dialect other than SqlServer. Only SQL
// Server sets a RETURN value; elsewhere the generated read throws at run time. Without a declared
// dialect nothing changes.
public class ZAO069Tests
{
    private const string Explicit = """
            [StoredProcedure("usp_Allocate")]
            public partial Task<(int Id, int Status)> AllocateAsync(
                int id, [Param(Direction = ParameterDirection.ReturnValue)] int status, CancellationToken ct);
        """;

    private const string Convention = """
            [StoredProcedure("usp_Allocate")]
            public partial Task<(int Id, int RETURN_VALUE)> AllocateAsync(
                int id, int RETURN_VALUE, CancellationToken ct);
        """;

    private static GeneratorDriverRunResult Run(string members, string repositoryAttribute, string assemblyAttribute = "")
        => GeneratorHarness.RunGenerator($$"""
            using System.Data;
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
            {{members}}
                }
            }
            """);

    private static Diagnostic[] Zao069(GeneratorDriverRunResult result)
        => result.Diagnostics.Where(d => string.Equals(d.Id, "ZAO069", StringComparison.Ordinal)).ToArray();

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    public void Explicit_ReturnValue_on_a_non_SqlServer_dialect_reports_ZAO069(string dialect)
    {
        var result = Run(Explicit, $"[Dialect(SqlDialect.{dialect})]");

        var zao069 = Assert.Single(Zao069(result));
        Assert.Equal(DiagnosticSeverity.Error, zao069.Severity);
        Assert.Equal(
            "Parameter 'status' of method 'AllocateAsync' is bound as the procedure's RETURN value, "
                + $"but the repository declares the {dialect} dialect, whose provider never sets one. "
                + "Only SQL Server has a procedure RETURN value. Read the value as an output parameter "
                + "instead: declare it OUT in the procedure and write "
                + "[Param(Direction = ParameterDirection.Output)].",
            zao069.GetMessage(CultureInfo.InvariantCulture));
        Assert.Empty(result.GeneratedTrees);
    }

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    public void RETURN_VALUE_convention_on_a_non_SqlServer_dialect_reports_ZAO069(string dialect)
    {
        var result = Run(Convention, $"[Dialect(SqlDialect.{dialect})]");

        var zao069 = Assert.Single(Zao069(result));
        Assert.Contains("'RETURN_VALUE'", zao069.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Empty(result.GeneratedTrees);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("[Dialect(SqlDialect.SqlServer)]", false)]
    [InlineData("[Dialect(SqlDialect.SqlServer)]", true)]
    [InlineData("[Dialect((SqlDialect)42)]", false)]
    [InlineData("[Dialect((SqlDialect)42)]", true)]
    public void No_ZAO069_without_a_dialect_or_on_SqlServer(string repositoryAttribute, bool convention)
    {
        var result = Run(convention ? Convention : Explicit, repositoryAttribute);

        Assert.Empty(Zao069(result));
        Assert.Single(result.GeneratedTrees);
    }

    [Fact]
    public void Assembly_default_and_repository_override_apply()
    {
        Assert.Single(Zao069(Run(Explicit, string.Empty, "[assembly: Dialect(SqlDialect.PostgreSql)]")));
        Assert.Empty(Zao069(Run(Explicit, "[Dialect(SqlDialect.SqlServer)]", "[assembly: Dialect(SqlDialect.PostgreSql)]")));
    }

    [Fact]
    public void Output_direction_on_a_RETURN_VALUE_field_reports_no_ZAO069()
    {
        var result = Run("""
                [StoredProcedure("usp_Allocate")]
                public partial Task<(int Id, int RETURN_VALUE)> AllocateAsync(
                    int id, [Param(Direction = ParameterDirection.Output)] int RETURN_VALUE, CancellationToken ct);
            """, "[Dialect(SqlDialect.PostgreSql)]");

        Assert.Empty(Zao069(result));
        Assert.Single(result.GeneratedTrees);
    }

    [Fact]
    public void Each_bound_parameter_is_reported_alongside_ZAO068()
    {
        var result = Run("""
                [StoredProcedure("usp_X")]
                public partial Task<(int A, int B)> RunAsync(
                    [Param(Direction = ParameterDirection.ReturnValue)] int a,
                    [Param(Direction = ParameterDirection.ReturnValue)] int b,
                    CancellationToken ct);
            """, "[Dialect(SqlDialect.PostgreSql)]");

        Assert.Equal(2, Zao069(result).Length);
        Assert.Contains(result.Diagnostics, d => string.Equals(d.Id, "ZAO068", StringComparison.Ordinal));
    }
}
