using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v2.0, #235 — type facets on stored-procedure output parameters.
//
// SqlClient validates an output parameter before it sends anything: with no
// DbType it infers NVarChar, and an NVarChar with Size 0 throws "the Size
// property has an invalid size of 0". The generator therefore sets DbType on
// every output and input-output parameter from the tuple element's type, and
// Size = -1, which is MAX, on string and byte[] outputs.
//
// `[Param]` carries optional Size, Precision, Scale and Direction members. The
// facets apply to any parameter they are written on, input included; an input
// parameter without them emits exactly what it did before.
public class ParamFacetTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Data;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public readonly record struct Money(decimal Amount, string Currency);
        public readonly record struct OrderId(int Value);
        public enum Status { Open = 1, Closed = 2 }
        public sealed record OrderRow(int CustomerId);

        """;

    private static GeneratorDriverRunResult Run(string members)
        => GeneratorHarness.RunGenerator(Usings
            + "public sealed partial class Repo(IAsyncDbConnection connection)\n{\n"
            + members + "\n}\n");

    private static string Generate(string members)
    {
        var (result, compile) = GeneratorHarness.RunGeneratorAndCompile(Usings
            + "public sealed partial class Repo(IAsyncDbConnection connection)\n{\n"
            + members + "\n}\n");
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(compile);
        return result.GeneratedTrees
            .Single(t => t.FilePath.EndsWith("Repo.g.cs", System.StringComparison.Ordinal))
            .GetText()
            .ToString();
    }

    private static Diagnostic[] Diagnostics(GeneratorDriverRunResult result, string id)
        => result.Diagnostics
            .Where(d => string.Equals(d.Id, id, System.StringComparison.Ordinal))
            .ToArray();

    [Theory]
    [InlineData("int", "Int32")]
    [InlineData("long", "Int64")]
    [InlineData("short", "Int16")]
    [InlineData("byte", "Byte")]
    [InlineData("bool", "Boolean")]
    [InlineData("double", "Double")]
    [InlineData("float", "Single")]
    [InlineData("Guid", "Guid")]
    [InlineData("DateTime", "DateTime2")]
    [InlineData("DateTimeOffset", "DateTimeOffset")]
    [InlineData("TimeSpan", "Time")]
    [InlineData("OrderId", "Int32")]
    [InlineData("Status", "Int32")]
    public void Output_parameter_gets_DbType_from_the_tuple_element_type(string type, string dbType)
    {
        var generated = Generate($$"""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, {{type}} Value)> RunAsync(int id, {{type}} value, CancellationToken ct);
            """);

        Assert.Contains("__p_value.DbType = global::System.Data.DbType." + dbType + ";", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("__p_value.Size", generated, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("string", "String")]
    [InlineData("byte[]", "Binary")]
    public void Variable_length_output_defaults_to_Size_minus_one(string type, string dbType)
    {
        var generated = Generate($$"""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, {{type}} Value)> RunAsync(int id, {{type}} value, CancellationToken ct);
            """);

        Assert.Contains("__p_value.DbType = global::System.Data.DbType." + dbType + ";", generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_value.Size = -1;", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Output_facets_from_Param_override_the_defaults()
    {
        var generated = Generate("""
            [StoredProcedure("usp_X")]
            public partial Task<(string Label, decimal Total)> RunAsync(
                [Param(Size = 50)] string label,
                [Param(Precision = 18, Scale = 4)] decimal total,
                CancellationToken ct);
            """);

        Assert.Contains("__p_label.Size = 50;", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("__p_label.Size = -1;", generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_total.DbType = global::System.Data.DbType.Decimal;", generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_total.Precision = 18;", generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_total.Scale = 4;", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Output_DbType_from_Param_overrides_the_inferred_one()
    {
        var generated = Generate("""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, string Code)> RunAsync(
                int id, [Param(DbType = DbType.AnsiString)] string code, CancellationToken ct);
            """);

        Assert.Contains("__p_code.DbType = global::System.Data.DbType.AnsiString;", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("DbType.String;", generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_code.Size = -1;", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Decimal_output_without_Scale_reports_warning_ZAO065()
    {
        var result = Run("""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, decimal Total)> RunAsync(int id, decimal total, CancellationToken ct);
            """);

        var zao065 = Assert.Single(Diagnostics(result, "ZAO065"));
        Assert.Equal(DiagnosticSeverity.Warning, zao065.Severity);
        Assert.Contains("total", zao065.GetMessage(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        // The code still emits and sets no precision: the provider decides.
        var generated = result.GeneratedTrees
            .Single(t => t.FilePath.EndsWith("Repo.g.cs", System.StringComparison.Ordinal))
            .GetText().ToString();
        Assert.Contains("__p_total.DbType = global::System.Data.DbType.Decimal;", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("__p_total.Precision", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("__p_total.Scale", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Decimal_output_with_Scale_reports_no_ZAO065()
    {
        var result = Run("""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, decimal Total)> RunAsync(
                int id, [Param(Precision = 18, Scale = 2)] decimal total, CancellationToken ct);
            """);

        Assert.Empty(Diagnostics(result, "ZAO065"));
    }

    [Fact]
    public void Decimal_input_parameter_reports_no_ZAO065()
    {
        var result = Run("""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, int Status)> RunAsync(decimal total, int status, CancellationToken ct);
            """);

        Assert.Empty(Diagnostics(result, "ZAO065"));
    }

    [Fact]
    public void InputOutput_direction_sends_the_value_and_reads_it_back()
    {
        var generated = Generate("""
            [StoredProcedure("usp_X")]
            public partial Task<(int Counter, string Label)> RunAsync(
                [Param(Direction = ParameterDirection.InputOutput)] int counter,
                string label,
                CancellationToken ct);
            """);

        Assert.Contains("__p_counter.Direction = global::System.Data.ParameterDirection.InputOutput;", generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_counter.DbType = global::System.Data.DbType.Int32;", generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_counter.Value = @counter;", generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_label.Direction = global::System.Data.ParameterDirection.Output;", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("__p_label.Value =", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_Output_direction_matches_the_convention()
    {
        var generated = Generate("""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, int Status)> RunAsync(
                int id, [Param(Direction = ParameterDirection.Output)] int status, CancellationToken ct);
            """);

        Assert.Contains("__p_status.Direction = global::System.Data.ParameterDirection.Output;", generated, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ParameterDirection.Output")]
    [InlineData("ParameterDirection.InputOutput")]
    [InlineData("ParameterDirection.ReturnValue")]
    public void Output_direction_on_a_parameter_with_no_tuple_field_reports_ZAO066(string direction)
    {
        var result = Run($$"""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, int Status)> RunAsync(
                int id, int status, [Param(Direction = {{direction}})] int extra, CancellationToken ct);
            """);

        var zao066 = Assert.Single(Diagnostics(result, "ZAO066"));
        Assert.Equal(DiagnosticSeverity.Error, zao066.Severity);
        Assert.Contains("extra", zao066.GetMessage(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Output_direction_on_a_Query_parameter_reports_ZAO066()
    {
        var result = Run("""
            [Query("SELECT @id")]
            public partial Task<int> GetAsync([Param(Direction = ParameterDirection.Output)] int id, CancellationToken ct);
            """);

        Assert.Single(Diagnostics(result, "ZAO066"));
    }

    // ReturnValue on a tuple-matched parameter is supported since #241; see
    // ReturnValueTests, ZAO067Tests and ZAO068Tests.
    [Fact]
    public void Input_direction_on_a_tuple_output_reports_ZAO066()
    {
        var result = Run("""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, int Status)> RunAsync(
                int id, [Param(Direction = ParameterDirection.Input)] int status, CancellationToken ct);
            """);

        Assert.Single(Diagnostics(result, "ZAO066"));
    }

    [Theory]
    [InlineData("DbType = DbType.Decimal", "DbType")]
    [InlineData("Size = 10", "Size")]
    [InlineData("Precision = 18", "Precision")]
    [InlineData("Scale = 2", "Scale")]
    [InlineData("Direction = ParameterDirection.Output", "Direction")]
    public void Facets_on_a_composite_parameter_report_ZAO066(string member, string memberName)
    {
        var result = Run($$"""
            [Query("SELECT @total_Amount")]
            public partial Task<int> GetAsync([Param({{member}})] Money total, CancellationToken ct);
            """);

        var zao066 = Assert.Single(Diagnostics(result, "ZAO066"));
        var message = zao066.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("[Param(" + memberName + ")]", message, System.StringComparison.Ordinal);
        Assert.Contains("composite", message, System.StringComparison.Ordinal);
    }

    // Name on a composite is ZAO063's; ZAO066 does not report it a second time.
    [Fact]
    public void Name_on_a_composite_parameter_reports_ZAO063_not_ZAO066()
    {
        var result = Run("""
            [Query("SELECT @total_Amount")]
            public partial Task<int> GetAsync([Param(Name = "t")] Money total, CancellationToken ct);
            """);

        Assert.Single(Diagnostics(result, "ZAO063"));
        Assert.Empty(Diagnostics(result, "ZAO066"));
    }

    // CancellationToken, transaction and BulkInsert collection parameters bind no
    // DbParameter of their own, so every [Param] member on them would be dropped.
    public static TheoryData<string, string> EveryMember() => new()
    {
        { "Name = \"x\"", "Name" },
        { "DbType = DbType.Int32", "DbType" },
        { "Size = 10", "Size" },
        { "Precision = 18", "Precision" },
        { "Scale = 2", "Scale" },
        { "Direction = ParameterDirection.Output", "Direction" },
    };

    [Theory]
    [MemberData(nameof(EveryMember))]
    public void Any_member_on_a_CancellationToken_reports_ZAO066(string member, string memberName)
    {
        var result = Run($$"""
            [Query("SELECT @id")]
            public partial Task<int> GetAsync(int id, [Param({{member}})] CancellationToken ct);
            """);

        var zao066 = Assert.Single(Diagnostics(result, "ZAO066"));
        var message = zao066.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("[Param(" + memberName + ")]", message, System.StringComparison.Ordinal);
        Assert.Contains("CancellationToken", message, System.StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryMember))]
    public void Any_member_on_a_transaction_reports_ZAO066(string member, string memberName)
    {
        var result = Run($$"""
            [Command("UPDATE t SET x = 1 WHERE id = @id")]
            public partial Task<int> UpdateAsync(int id, [Param({{member}})] IAsyncDbTransaction tx, CancellationToken ct);
            """);

        var zao066 = Assert.Single(Diagnostics(result, "ZAO066"));
        var message = zao066.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("[Param(" + memberName + ")]", message, System.StringComparison.Ordinal);
        Assert.Contains("transaction", message, System.StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryMember))]
    public void Any_member_on_a_BulkInsert_collection_reports_ZAO066(string member, string memberName)
    {
        var result = Run($$"""
            [Command("INSERT INTO Orders (CustomerId) VALUES (@CustomerId)", Kind = CommandKind.BulkInsert)]
            public partial Task<int> InsertAsync([Param({{member}})] IReadOnlyList<OrderRow> orders, CancellationToken ct);
            """);

        var zao066 = Assert.Single(Diagnostics(result, "ZAO066"));
        var message = zao066.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("[Param(" + memberName + ")]", message, System.StringComparison.Ordinal);
        Assert.Contains("BulkInsert", message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Several_members_on_a_CancellationToken_are_listed_in_one_ZAO066()
    {
        var result = Run("""
            [Query("SELECT @id")]
            public partial Task<int> GetAsync(int id, [Param(Name = "c", Size = 4)] CancellationToken ct);
            """);

        var zao066 = Assert.Single(Diagnostics(result, "ZAO066"));
        Assert.Contains("[Param(Name, Size)]", zao066.GetMessage(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Unannotated_CancellationToken_transaction_and_collection_report_no_ZAO066()
    {
        var result = Run("""
            [Command("UPDATE t SET x = 1 WHERE id = @id")]
            public partial Task<int> UpdateAsync(int id, IAsyncDbTransaction tx, CancellationToken ct);

            [Command("INSERT INTO Orders (CustomerId) VALUES (@CustomerId)", Kind = CommandKind.BulkInsert)]
            public partial Task<int> InsertAsync(IReadOnlyList<OrderRow> orders, CancellationToken ct);
            """);

        Assert.Empty(Diagnostics(result, "ZAO066"));
    }

    // SqlClient rejects an output of a fixed-length type, NCHAR or CHAR, with a
    // size of 0: "the Size property has an invalid size of 0". The generator gives
    // it no default, because -1 would declare a MAX type instead of the fixed
    // length written.
    [Theory]
    [InlineData("DbType = DbType.StringFixedLength", "DbType = StringFixedLength")]
    [InlineData("DbType = DbType.AnsiStringFixedLength", "DbType = AnsiStringFixedLength")]
    [InlineData("DbType = DbType.StringFixedLength, Size = 0", "DbType = StringFixedLength, Size = 0")]
    public void Fixed_length_output_without_a_Size_reports_ZAO066(string facets, string shown)
    {
        var result = Run($$"""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, string Code)> RunAsync(
                int id, [Param({{facets}})] string code, CancellationToken ct);
            """);

        var zao066 = Assert.Single(Diagnostics(result, "ZAO066"));
        var message = zao066.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("[Param(" + shown + ")]", message, System.StringComparison.Ordinal);
        Assert.Contains("fixed-length", message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Fixed_length_InputOutput_without_Size_reports_ZAO066()
    {
        var result = Run("""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, string Code)> RunAsync(
                int id,
                [Param(DbType = DbType.StringFixedLength, Direction = ParameterDirection.InputOutput)] string code,
                CancellationToken ct);
            """);

        Assert.Single(Diagnostics(result, "ZAO066"));
    }

    // An explicit -1 is the author's choice, and SqlClient accepts it; the
    // integration tests pin that.
    [Theory]
    [InlineData("10")]
    [InlineData("-1")]
    public void Fixed_length_output_with_a_Size_emits_it(string size)
    {
        var generated = Generate($$"""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, string Code)> RunAsync(
                int id, [Param(DbType = DbType.StringFixedLength, Size = {{size}})] string code, CancellationToken ct);
            """);

        Assert.Contains("__p_code.DbType = global::System.Data.DbType.StringFixedLength;", generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_code.Size = " + size + ";", generated, System.StringComparison.Ordinal);
    }

    // A fixed-length DbType on an input needs no Size: the provider sizes it from
    // the value.
    [Fact]
    public void Fixed_length_input_without_Size_reports_no_ZAO066()
    {
        var result = Run("""
            [Query("SELECT COUNT(*) FROM t WHERE code = @code")]
            public partial Task<int> CountAsync([Param(DbType = DbType.StringFixedLength)] string code, CancellationToken ct);
            """);

        Assert.Empty(Diagnostics(result, "ZAO066"));
    }

    [Theory]
    [InlineData("string", "")]
    [InlineData("byte[]", "")]
    [InlineData("string", "DbType = DbType.AnsiString, ")]
    public void Size_zero_on_a_variable_length_output_reports_ZAO066(string type, string dbType)
    {
        var result = Run($$"""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, {{type}} Value)> RunAsync(
                int id, [Param({{dbType}}Size = 0)] {{type}} value, CancellationToken ct);
            """);

        var zao066 = Assert.Single(Diagnostics(result, "ZAO066"));
        Assert.Contains("[Param(Size = 0)]", zao066.GetMessage(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }

    // Size = 0 means "infer" on an input and has no meaning on a fixed-size
    // output such as int, so neither is reported.
    [Fact]
    public void Size_zero_on_an_input_or_a_fixed_size_output_reports_no_ZAO066()
    {
        var result = Run("""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, int Status)> RunAsync(
                [Param(Size = 0)] string code, int id, [Param(Size = 0)] int status, CancellationToken ct);
            """);

        Assert.Empty(Diagnostics(result, "ZAO066"));
    }

    [Fact]
    public void Size_below_minus_one_reports_ZAO066()
    {
        var result = Run("""
            [Query("SELECT COUNT(*) FROM t WHERE code = @code")]
            public partial Task<int> CountAsync([Param(Size = -2)] string code, CancellationToken ct);
            """);

        var zao066 = Assert.Single(Diagnostics(result, "ZAO066"));
        Assert.Contains("[Param(Size = -2)]", zao066.GetMessage(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Input_parameter_facets_apply()
    {
        var generated = Generate("""
            [Query("SELECT COUNT(*) FROM t WHERE code = @code AND total = @total")]
            public partial Task<int> CountAsync(
                [Param(DbType = DbType.AnsiString, Size = 20)] string code,
                [Param(Precision = 18, Scale = 2)] decimal total,
                CancellationToken ct);
            """);

        Assert.Contains("__p_code.DbType = global::System.Data.DbType.AnsiString;", generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_code.Size = 20;", generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_total.Precision = 18;", generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_total.Scale = 2;", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("__p_total.DbType", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain(".Direction", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Input_parameter_facets_apply_in_batch_commands()
    {
        var generated = Generate("""
            [Query("SELECT COUNT(*) FROM a WHERE code = @code; SELECT COUNT(*) FROM b WHERE code = @code", Batch = BatchMode.Always)]
            public partial Task<(int A, int B)?> CountAsync([Param(Size = 20)] string code, CancellationToken ct);
            """);

        Assert.Contains("__p_code_0.Size = 20;", generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_code_1.Size = 20;", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Input_parameter_without_facets_emits_no_facet_lines()
    {
        var generated = Generate("""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, int Status)> RunAsync(
                string code, decimal total, DateTime at, int id, int status, CancellationToken ct);
            """);

        foreach (var name in new[] { "code", "total", "at" })
        {
            Assert.DoesNotContain("__p_" + name + ".DbType", generated, System.StringComparison.Ordinal);
            Assert.DoesNotContain("__p_" + name + ".Size", generated, System.StringComparison.Ordinal);
            Assert.DoesNotContain("__p_" + name + ".Direction", generated, System.StringComparison.Ordinal);
        }
    }
}
