using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// #351 — parameter names spelled per dialect. #219 made every emitted
// `DbParameter.ParameterName` bare. Each provider resolves a name most cheaply in its
// own spelling, so the emit now follows the repository's declared dialect:
//
//   * Sqlite and SqlServer: the sigil the SQL placeholder writes, `@id` for `@id`, and
//     `@` when the SQL has no placeholder for it, as for a stored procedure.
//     Microsoft.Data.Sqlite otherwise tries each prefix in front of a bare name.
//   * PostgreSql and MySql: the bare name. Npgsql and MySqlConnector strip a sigil
//     with a Substring.
//   * No declared dialect: the token the SQL holds, so `@id` for `@id`, and the bare
//     name when the SQL has none. A [LinqQuery] renders PostgreSQL, so it binds bare.
//
// These tests pin the rule for every emit path that names a parameter: scalar,
// value object, composite, BulkInsert, Identity, stored procedure with output
// parameters, and the [Param(Name)] override.
public class ParameterNamePrefixTests
{
    private const string Usings = """
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public readonly record struct Money(decimal Amount, string Currency);
        public sealed record OrderRow(int Id, int CustomerId, decimal Total);
        public sealed record BulkRow(int CustomerId, decimal Total);

        """;

    // Any string literal assigned to ParameterName, captured whole. A prefixed
    // name is one whose first character is a provider sigil.
    private static readonly Regex ParameterNameLiteral = new(
        "\\.ParameterName = \"(?<name>[^\"]*)\"",
        RegexOptions.CultureInvariant,
        System.TimeSpan.FromSeconds(1));

    private static string Generate(string members, string? dialect = null, bool assemblyLevel = false)
    {
        var attribute = dialect is null ? string.Empty : $"[Dialect(SqlDialect.{dialect})]\n";
        var source = (assemblyLevel ? Usings.Replace("namespace TestApp;", attribute.Replace("[", "[assembly: ") + "namespace TestApp;", System.StringComparison.Ordinal) : Usings)
            + (assemblyLevel ? string.Empty : attribute)
            + "public sealed partial class Repo(IAsyncDbConnection connection)\n{\n"
            + members + "\n}\n";
        var result = GeneratorHarness.RunGenerator(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        return result.GeneratedTrees
            .Single(t => t.FilePath.EndsWith("Repo.g.cs", System.StringComparison.Ordinal))
            .GetText()
            .ToString();
    }

    private static string[] EmittedNames(string generated)
    {
        var names = ParameterNameLiteral.Matches(generated)
            .Select(m => m.Groups["name"].Value)
            .ToArray();
        Assert.NotEmpty(names);
        return names;
    }

    private static string Joined(string generated, bool sorted = false)
    {
        var names = EmittedNames(generated);
        if (sorted) System.Array.Sort(names, System.StringComparer.Ordinal);
        return string.Join(",", names);
    }

    private const string ScalarQuery = """
        [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
        public partial Task<OrderRow?> GetAsync(int id, CancellationToken ct);
        """;

    private const string StoredProcedure = """
        [StoredProcedure("usp_InsertOrder")]
        public partial Task<(OrderRow Result, int NewOrderId)> InsertAsync(
            int customerId, int newOrderId, CancellationToken ct);
        """;

    [Theory]
    [InlineData(null, "@id")]
    [InlineData("Sqlite", "@id")]
    [InlineData("SqlServer", "@id")]
    [InlineData("PostgreSql", "id")]
    [InlineData("MySql", "id")]
    public void Scalar_parameter_is_named_per_dialect(string? dialect, string expected)
    {
        Assert.Equal(expected, Joined(Generate(ScalarQuery, dialect)));
    }

    [Theory]
    [InlineData("Sqlite", "@id")]
    [InlineData("PostgreSql", "id")]
    public void Assembly_level_dialect_names_the_parameters(string dialect, string expected)
    {
        Assert.Equal(expected, Joined(Generate(ScalarQuery, dialect, assemblyLevel: true)));
    }

    [Theory]
    [InlineData(null, ":id")]
    [InlineData(null, "$id")]
    [InlineData("Sqlite", ":id")]
    [InlineData("Sqlite", "$id")]
    public void Sigil_the_SQL_writes_is_kept(string? dialect, string placeholder)
    {
        var generated = Generate($$"""
            [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = {{placeholder}}")]
            public partial Task<OrderRow?> GetAsync(int id, CancellationToken ct);
            """, dialect);

        Assert.Equal(placeholder, Joined(generated));
    }

    [Theory]
    [InlineData("SELECT 1 WHERE @@id = @idx AND @id = 1", '@')]
    [InlineData("SELECT x::id FROM t WHERE y = $id", '$')]
    [InlineData("SELECT 1 WHERE a = @ID", '@')]
    [InlineData("SELECT 1 WHERE a = @idx OR b = x@id", null)]
    [InlineData("SELECT @@id", null)]
    public void Placeholder_sigil_is_found_as_a_whole_word(string sql, char? expected)
    {
        Assert.Equal(expected, OrmGenerator.FindPlaceholderSigil(sql, "id"));
    }

    [Theory]
    [InlineData(null, "@id,@total_Amount,@total_Currency")]
    [InlineData("PostgreSql", "id,total_Amount,total_Currency")]
    public void Composite_parameter_fields_are_named_per_dialect(string? dialect, string expected)
    {
        var generated = Generate("""
            [Command("UPDATE Orders SET Amount = @total_Amount, Currency = @total_Currency WHERE Id = @id", Kind = CommandKind.NonQuery)]
            public partial Task<int> UpdateAsync(int id, Money total, CancellationToken ct);
            """, dialect);

        Assert.Equal(expected, Joined(generated, sorted: true));
    }

    [Theory]
    [InlineData(null, "@customerId")]
    [InlineData("PostgreSql", "customerId")]
    public void Identity_command_is_named_per_dialect(string? dialect, string expected)
    {
        var generated = Generate("""
            [Command("INSERT INTO Orders (CustomerId) VALUES (@customerId) RETURNING Id", Kind = CommandKind.Identity)]
            public partial Task<int> InsertAsync(int customerId, CancellationToken ct);
            """, dialect);

        Assert.Equal(expected, Joined(generated));
    }

    // A procedure's CommandText holds no placeholder, so without a declared dialect the
    // names stay bare, and SqlServer gets the `@` its own parameter list writes.
    [Theory]
    [InlineData(null, "customerId,newOrderId")]
    [InlineData("SqlServer", "@customerId,@newOrderId")]
    public void Stored_procedure_parameters_are_named_per_dialect(string? dialect, string expected)
    {
        Assert.Equal(expected, Joined(Generate(StoredProcedure, dialect)));
    }

    [Fact]
    public void BulkInsert_names_carry_the_sigil_the_built_SQL_writes()
    {
        var generated = Generate("""
            [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)", Kind = CommandKind.BulkInsert)]
            public partial Task<int> InsertManyAsync(IReadOnlyList<BulkRow> rows, CancellationToken ct);
            """);

        // #326 — each row reads its name from a static array the cache class fills
        // once with `@<placeholder>_<row>`, and the SQL is built from the same names.
        Assert.Contains("Names_CustomerId_0 = BuildNames(\"@CustomerId_\");", generated, System.StringComparison.Ordinal);
        Assert.Contains("Names_Total_1 = BuildNames(\"@Total_\");", generated, System.StringComparison.Ordinal);
        Assert.Contains("sb.Append('(').Append(Names_CustomerId_0[i]).Append(\", \").Append(Names_Total_1[i])", generated, System.StringComparison.Ordinal);
        Assert.Contains(".ParameterName = __ZaoBulkInsert0_InsertManyAsync.Names_CustomerId_0[__i];", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void BulkInsert_names_are_bare_on_PostgreSql()
    {
        var generated = Generate("""
            [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)", Kind = CommandKind.BulkInsert)]
            public partial Task<int> InsertManyAsync(IReadOnlyList<BulkRow> rows, CancellationToken ct);
            """, "PostgreSql");

        Assert.Contains("Names_CustomerId_0 = BuildNames(\"CustomerId_\");", generated, System.StringComparison.Ordinal);
        Assert.Contains("sb.Append('(').Append('@').Append(Names_CustomerId_0[i]).Append(\", \").Append('@').Append(Names_Total_1[i])", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("BuildNames(\"@", generated, System.StringComparison.Ordinal);
    }

    // The override is stored bare, #219, and then named like any other parameter.
    [Theory]
    [InlineData("@orderId", null, "@orderId")]
    [InlineData(":orderId", null, "@orderId")]
    [InlineData("$orderId", null, "@orderId")]
    [InlineData("orderId", null, "@orderId")]
    [InlineData("@orderId", "PostgreSql", "orderId")]
    public void Param_name_override_is_named_like_the_SQL_placeholder(string overrideName, string? dialect, string expected)
    {
        var generated = Generate($$"""
            [Query("SELECT 1 WHERE @orderId = 42")]
            public partial Task<int> SearchAsync([Param(Name = "{{overrideName}}")] int id, CancellationToken ct);
            """, dialect);

        Assert.Equal(expected, Joined(generated));
    }
}
