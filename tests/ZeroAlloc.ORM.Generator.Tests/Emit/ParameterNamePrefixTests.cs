using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v2.0 — provider-neutral parameter names, issue #219.
//
// The SQL placeholder carries the provider's sigil, which the author writes:
// `@id` for SQL Server, SQLite and Postgres, `:id` for Oracle. The emitted
// `DbParameter.ParameterName` carries no sigil at all, so the same generated
// code binds against whichever placeholder form the SQL uses. Every supported
// provider matches an unprefixed ParameterName to a prefixed placeholder.
//
// These tests pin the rule for every emit path that names a parameter: scalar,
// value object, composite, composite in batch mode, BulkInsert, Identity,
// stored procedure with output parameters, and the [Param(Name)] override.
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

    private static string Generate(string members)
    {
        var source = Usings + "public sealed partial class Repo(IAsyncDbConnection connection)\n{\n"
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

    private static void AssertNoPrefix(string generated)
    {
        foreach (var name in EmittedNames(generated))
        {
            Assert.False(
                name.Length > 0 && (name[0] == '@' || name[0] == ':' || name[0] == '$'),
                $"Emitted ParameterName \"{name}\" carries a provider sigil.");
        }
    }

    [Fact]
    public void Scalar_parameter_emits_unprefixed_name()
    {
        var generated = Generate("""
            [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
            public partial Task<OrderRow?> GetAsync(int id, CancellationToken ct);
            """);

        Assert.Equal("id", Joined(generated));
        AssertNoPrefix(generated);
    }

    [Fact]
    public void Composite_parameter_emits_unprefixed_field_names()
    {
        var generated = Generate("""
            [Command("UPDATE Orders SET Amount = @total_Amount, Currency = @total_Currency WHERE Id = @id", Kind = CommandKind.NonQuery)]
            public partial Task<int> UpdateAsync(int id, Money total, CancellationToken ct);
            """);

        Assert.Equal("id,total_Amount,total_Currency", Joined(generated, sorted: true));
        AssertNoPrefix(generated);
    }

    [Fact]
    public void Identity_command_emits_unprefixed_name()
    {
        var generated = Generate("""
            [Command("INSERT INTO Orders (CustomerId) VALUES (@customerId) RETURNING Id", Kind = CommandKind.Identity)]
            public partial Task<int> InsertAsync(int customerId, CancellationToken ct);
            """);

        Assert.Equal("customerId", Joined(generated));
    }

    [Fact]
    public void Stored_procedure_with_output_parameter_emits_unprefixed_names()
    {
        var generated = Generate("""
            [StoredProcedure("usp_InsertOrder")]
            public partial Task<(OrderRow Result, int NewOrderId)> InsertAsync(
                int customerId, int newOrderId, CancellationToken ct);
            """);

        Assert.Equal("customerId,newOrderId", Joined(generated));
    }

    [Fact]
    public void BulkInsert_emits_unprefixed_row_indexed_names()
    {
        // The SQL text the chunk loop builds keeps the author's `@` placeholder;
        // only the ParameterName side drops it.
        var generated = Generate("""
            [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)", Kind = CommandKind.BulkInsert)]
            public partial Task<int> InsertManyAsync(IReadOnlyList<BulkRow> rows, CancellationToken ct);
            """);

        Assert.Contains(".ParameterName = \"CustomerId_\" + __i.ToString(", generated, System.StringComparison.Ordinal);
        Assert.Contains(".ParameterName = \"Total_\" + __i.ToString(", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain(".ParameterName = \"@", generated, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("@orderId")]
    [InlineData(":orderId")]
    [InlineData("$orderId")]
    [InlineData("orderId")]
    public void Param_name_override_is_emitted_without_its_sigil(string overrideName)
    {
        var generated = Generate($$"""
            [Query("SELECT 1 WHERE @orderId = 42")]
            public partial Task<int> SearchAsync([Param(Name = "{{overrideName}}")] int id, CancellationToken ct);
            """);

        Assert.Equal("orderId", Joined(generated));
    }
}
