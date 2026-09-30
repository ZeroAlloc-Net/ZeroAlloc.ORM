using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// #248 — ZAO015 reports a `[Param(DbType = ...)]` that the declared dialect's provider rejects
// for the type the parameter binds as. The dialect comes from `[Dialect]` on the repository,
// else from `[assembly: Dialect]`. Without either, ZAO015 never fires: no DbType is rejected by
// every provider, so any report would break code that works on some provider (#243).
public class ZAO015Tests
{
    private const string Usings = """
        using System;
        using System.Data;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        """;

    private const string Types = """

        namespace TestApp
        {
            public enum Status { Open = 1, Closed = 2 }
            [StoreAsString] public enum Kind { A, B }
            public readonly record struct OrderId(int Value);
        }
        """;

    // A repository in namespace TestApp with the given attribute line above it.
    private static string Source(string repositoryAttribute, string members, string assemblyAttribute = "")
        => Usings
            + assemblyAttribute + "\n"
            + "namespace TestApp\n{\n"
            + repositoryAttribute + "\n"
            + "public sealed partial class Repo(IAsyncDbConnection connection)\n{\n"
            + members
            + "\n}\n}\n"
            + Types;

    private static GeneratorDriverRunResult RunResult(string source) => GeneratorHarness.RunGenerator(source);

    private static Diagnostic[] Zao015(string source)
        => RunResult(source).Diagnostics
            .Where(d => string.Equals(d.Id, "ZAO015", StringComparison.Ordinal))
            .ToArray();

    private static string Query(string parameter)
        => $$"""
            [Query("SELECT 1 WHERE @p = @p")]
            public partial Task<int> GetAsync({{parameter}}, CancellationToken ct);
            """;

    [Theory]
    [InlineData("SqlServer", "int", "Guid")]
    [InlineData("SqlServer", "string", "Guid")]
    [InlineData("SqlServer", "Guid", "Int32")]
    [InlineData("SqlServer", "DateTime", "Int32")]
    [InlineData("SqlServer", "byte[]", "String")]
    [InlineData("SqlServer", "int", "UInt32")]
    [InlineData("PostgreSql", "int", "String")]
    [InlineData("PostgreSql", "bool", "Int32")]
    [InlineData("PostgreSql", "string", "Int32")]
    [InlineData("PostgreSql", "DateTime", "String")]
    [InlineData("PostgreSql", "Guid", "String")]
    [InlineData("PostgreSql", "TimeSpan", "DateTime")]
    public void Rejected_pair_on_a_repository_dialect_reports_ZAO015(string dialect, string type, string dbType)
    {
        var diagnostics = Zao015(Source(
            $"[Dialect(SqlDialect.{dialect})]",
            Query($"[Param(DbType = DbType.{dbType})] {type} p")));

        var zao015 = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, zao015.Severity);
        var message = zao015.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("DbType = " + dbType, message, StringComparison.Ordinal);
        Assert.Contains("'p'", message, StringComparison.Ordinal);
        Assert.Contains("'GetAsync'", message, StringComparison.Ordinal);
        Assert.Contains(dialect, message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SqlServer", "int", "String")]
    [InlineData("SqlServer", "int", "Int64")]
    [InlineData("SqlServer", "string", "AnsiString")]
    [InlineData("SqlServer", "DateTime", "String")]
    [InlineData("SqlServer", "decimal", "Currency")]
    [InlineData("PostgreSql", "int", "Int64")]
    [InlineData("PostgreSql", "int", "Date")]
    [InlineData("PostgreSql", "string", "AnsiString")]
    [InlineData("PostgreSql", "byte[]", "String")]
    [InlineData("PostgreSql", "DateTime", "DateTime2")]
    public void Accepted_pair_reports_no_ZAO015(string dialect, string type, string dbType)
    {
        Assert.Empty(Zao015(Source(
            $"[Dialect(SqlDialect.{dialect})]",
            Query($"[Param(DbType = DbType.{dbType})] {type} p"))));
    }

    [Fact]
    public void Message_names_the_type_the_provider_and_the_dialect()
    {
        var zao015 = Assert.Single(Zao015(Source(
            "[Dialect(SqlDialect.SqlServer)]",
            Query("[Param(DbType = DbType.Guid)] int p"))));

        Assert.Equal(
            "[Param(DbType = Guid)] on parameter 'p' of method 'GetAsync' fails at run time: "
                + "Microsoft.Data.SqlClient, the provider of the declared SqlServer dialect, "
                + "rejects DbType Guid for a value of type 'int'",
            zao015.GetMessage(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("Sqlite", "Guid")]
    [InlineData("Sqlite", "Binary")]
    [InlineData("MySql", "Guid")]
    [InlineData("MySql", "Binary")]
    public void Dialect_whose_provider_rejects_nothing_reports_no_ZAO015(string dialect, string dbType)
    {
        Assert.Empty(Zao015(Source(
            $"[Dialect(SqlDialect.{dialect})]",
            Query($"[Param(DbType = DbType.{dbType})] int p"))));
        Assert.Empty(Zao015(Source(
            $"[Dialect(SqlDialect.{dialect})]",
            Query("[Param(DbType = (DbType)999)] int p"))));
    }

    [Theory]
    [InlineData("[Param(DbType = DbType.Guid)] int p")]
    [InlineData("[Param(DbType = DbType.Int32)] Guid p")]
    [InlineData("[Param(DbType = DbType.String)] int p")]
    [InlineData("[Param(DbType = (DbType)999)] int p")]
    public void No_declared_dialect_reports_no_ZAO015(string parameter)
    {
        var result = RunResult(Source(string.Empty, Query(parameter)));

        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZAO015", StringComparison.Ordinal));
        Assert.Single(result.GeneratedTrees);
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    public void Undefined_DbType_value_reports_ZAO015_for_any_bound_type(string dialect)
    {
        // DateOnly is outside the probed table, but an undefined value fails on assignment.
        foreach (var type in new[] { "int", "string", "DateOnly" })
        {
            var zao015 = Assert.Single(Zao015(Source(
                $"[Dialect(SqlDialect.{dialect})]",
                Query($"[Param(DbType = (DbType)999)] {type} p"))));
            Assert.Contains("DbType = (DbType)999", zao015.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("DateOnly", "Guid")]
    [InlineData("TimeOnly", "Guid")]
    public void Unprobed_bound_type_reports_no_ZAO015_for_a_defined_DbType(string type, string dbType)
    {
        Assert.Empty(Zao015(Source(
            "[Dialect(SqlDialect.SqlServer)]",
            Query($"[Param(DbType = DbType.{dbType})] {type} p"))));
    }

    [Fact]
    public void Command_and_stored_procedure_parameters_are_checked()
    {
        var diagnostics = Zao015(Source("[Dialect(SqlDialect.SqlServer)]", """
            [Command("UPDATE T SET A = @p")]
            public partial Task<int> UpdateAsync([Param(DbType = DbType.Guid)] int p, CancellationToken ct);

            [StoredProcedure("usp_X")]
            public partial Task<int> RunAsync([Param(DbType = DbType.Guid)] int q, CancellationToken ct);
            """));

        Assert.Equal(2, diagnostics.Length);
        Assert.Contains(diagnostics, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("'UpdateAsync'", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("'RunAsync'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("int?", "int")]
    [InlineData("Status", "int")]
    [InlineData("Status?", "int")]
    [InlineData("Kind", "string")]
    [InlineData("OrderId", "int")]
    public void Parameter_is_checked_as_the_primitive_it_binds_as(string type, string boundType)
    {
        var zao015 = Assert.Single(Zao015(Source(
            "[Dialect(SqlDialect.SqlServer)]",
            Query($"[Param(DbType = DbType.Guid)] {type} p"))));

        Assert.Contains($"a value of type '{boundType}'", zao015.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Assembly_default_applies_to_a_repository_without_its_own_attribute()
    {
        Assert.Single(Zao015(Source(
            string.Empty,
            Query("[Param(DbType = DbType.Guid)] int p"),
            assemblyAttribute: "[assembly: Dialect(SqlDialect.SqlServer)]")));
    }

    [Fact]
    public void Repository_attribute_overrides_the_assembly_default()
    {
        // Npgsql rejects an int sent as DbType.String, SqlClient accepts it.
        const string Parameter = "[Param(DbType = DbType.String)] int p";

        Assert.Empty(Zao015(Source(
            "[Dialect(SqlDialect.SqlServer)]",
            Query(Parameter),
            assemblyAttribute: "[assembly: Dialect(SqlDialect.PostgreSql)]")));

        Assert.Single(Zao015(Source(
            "[Dialect(SqlDialect.PostgreSql)]",
            Query(Parameter),
            assemblyAttribute: "[assembly: Dialect(SqlDialect.SqlServer)]")));

        Assert.Empty(Zao015(Source(
            "[Dialect(SqlDialect.Sqlite)]",
            Query("[Param(DbType = DbType.Guid)] int p"),
            assemblyAttribute: "[assembly: Dialect(SqlDialect.SqlServer)]")));
    }

    [Fact]
    public void Each_repository_uses_its_own_dialect()
    {
        var source = Usings + """
            namespace TestApp
            {
                [Dialect(SqlDialect.PostgreSql)]
                public sealed partial class PgRepo(IAsyncDbConnection connection)
                {
                    [Query("SELECT @p")]
                    public partial Task<int> GetAsync([Param(DbType = DbType.String)] int p, CancellationToken ct);
                }

                [Dialect(SqlDialect.SqlServer)]
                public sealed partial class SqlRepo(IAsyncDbConnection connection)
                {
                    [Query("SELECT @p")]
                    public partial Task<int> GetAsync([Param(DbType = DbType.String)] int p, CancellationToken ct);
                }
            }
            """;

        var zao015 = Assert.Single(Zao015(source));
        Assert.Contains("PostgreSql", zao015.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Attribute_on_another_partial_declaration_of_the_repository_applies()
    {
        var repo = CSharpSyntaxTree.ParseText(Source(string.Empty, Query("[Param(DbType = DbType.Guid)] int p")), path: "/src/Repo.cs");
        var dialect = CSharpSyntaxTree.ParseText("""
            using ZeroAlloc.ORM;
            namespace TestApp;
            [Dialect(SqlDialect.SqlServer)]
            public sealed partial class Repo;
            """, path: "/src/Repo.Dialect.cs");

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ZeroAlloc.ORM.Generator.OrmGenerator());
        driver = driver.RunGenerators(GeneratorHarness.CreateCompilation(new[] { repo, dialect }));

        Assert.Single(driver.GetRunResult().Diagnostics, d => string.Equals(d.Id, "ZAO015", StringComparison.Ordinal));
    }

    [Fact]
    public void Attribute_on_a_containing_type_or_a_base_type_does_not_apply()
    {
        var source = Usings + """
            namespace TestApp
            {
                [Dialect(SqlDialect.SqlServer)]
                public partial class Outer
                {
                    public sealed partial class Repo(IAsyncDbConnection connection)
                    {
                        [Query("SELECT @p")]
                        public partial Task<int> GetAsync([Param(DbType = DbType.Guid)] int p, CancellationToken ct);
                    }
                }

                [Dialect(SqlDialect.SqlServer)]
                public abstract class RepoBase;

                public sealed partial class Derived(IAsyncDbConnection connection) : RepoBase
                {
                    [Query("SELECT @p")]
                    public partial Task<int> GetAsync([Param(DbType = DbType.Guid)] int p, CancellationToken ct);
                }
            }
            """;

        Assert.Empty(Zao015(source));
    }

    [Fact]
    public void Output_parameter_is_not_checked_and_input_output_is()
    {
        var diagnostics = Zao015(Source("[Dialect(SqlDialect.SqlServer)]", """
            [StoredProcedure("usp_Out")]
            public partial Task<(int Id, int Total)> OutAsync(
                int id, [Param(DbType = DbType.Guid)] int total, CancellationToken ct);

            [StoredProcedure("usp_InOut")]
            public partial Task<(int Id, int Total)> InOutAsync(
                int id,
                [Param(DbType = DbType.Guid, Direction = ParameterDirection.InputOutput)] int total,
                CancellationToken ct);
            """));

        var zao015 = Assert.Single(diagnostics);
        Assert.Contains("'InOutAsync'", zao015.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Parameter_with_a_ZAO066_reports_no_ZAO015()
    {
        var result = RunResult(Source(
            "[Dialect(SqlDialect.SqlServer)]",
            Query("[Param(DbType = DbType.Guid, Size = -2)] int p")));

        Assert.Contains(result.Diagnostics, d => string.Equals(d.Id, "ZAO066", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, d => string.Equals(d.Id, "ZAO015", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAO015_suppresses_the_repository_emit_like_every_error()
    {
        var result = RunResult(Source(
            "[Dialect(SqlDialect.SqlServer)]",
            Query("[Param(DbType = DbType.Guid)] int p")));

        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Dialect_value_outside_the_enum_declares_no_dialect()
    {
        Assert.Empty(Zao015(Source(
            "[Dialect((SqlDialect)42)]",
            Query("[Param(DbType = DbType.Guid)] int p"),
            assemblyAttribute: "[assembly: Dialect(SqlDialect.SqlServer)]")));
    }

    [Fact]
    public void Dialect_numbers_match_the_generator_model()
    {
        foreach (var dialect in Enum.GetValues<SqlDialect>())
        {
            var model = (SqlDialectModel)(int)dialect;
            Assert.True(Enum.IsDefined(model), dialect.ToString());
            Assert.Equal(dialect.ToString(), model.ToString());
        }
        Assert.Equal(Enum.GetValues<SqlDialect>().Length, Enum.GetValues<SqlDialectModel>().Length);
    }

    private static readonly string[] ProbedReaders =
    [
        "GetBoolean", "GetByte", "GetDateTime", "GetDecimal", "GetDouble",
        "GetFieldValue<byte[]>", "GetFieldValue<global::System.DateTimeOffset>",
        "GetFieldValue<global::System.TimeSpan>", "GetFloat", "GetGuid", "GetInt16",
        "GetInt32", "GetInt64", "GetString",
    ];

    [Fact]
    public void Probed_bound_types_are_the_catalog_readers_but_DateOnly_and_TimeOnly()
    {
        Assert.Equal(
            ProbedReaders,
            DialectDbTypeRejections.ProbedReaderMethods.OrderBy(r => r, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }
}
