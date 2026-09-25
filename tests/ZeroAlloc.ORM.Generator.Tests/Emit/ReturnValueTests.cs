using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// #241 — capturing a SQL Server procedure's RETURN value.
//
// SQL Server fills the RETURN value only into a parameter whose Direction is
// ParameterDirection.ReturnValue. An Output parameter is sent to the procedure as
// a named argument instead, so the documented `RETURN_VALUE` tuple field failed:
// the procedure has no @RETURN_VALUE parameter and rejects the call.
//
// A tuple-matched parameter binds as ReturnValue when `[Param]` says so, or by
// convention when its bound name is exactly `RETURN_VALUE`, its tuple field is
// `int` or `int?` and `[Param]` writes no Direction. A convention-named field of
// any other type stays an ordinary output parameter, as it was in 2.0.0.
public class ReturnValueTests
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

        public sealed record OrderRow(int Id, int CustomerId);

        """;

    private const string ReturnValueDirection = "global::System.Data.ParameterDirection.ReturnValue;";

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

    [Fact]
    public void Explicit_ReturnValue_direction_binds_a_return_value_parameter()
    {
        var generated = Generate("""
            [StoredProcedure("usp_X")]
            public partial Task<(OrderRow Row, int Doubled, int Status)> RunAsync(
                int seed,
                int doubled,
                [Param(Direction = ParameterDirection.ReturnValue)] int status,
                CancellationToken ct);
            """);

        Assert.Contains("__p_status.Direction = " + ReturnValueDirection, generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_status.DbType = global::System.Data.DbType.Int32;", generated, System.StringComparison.Ordinal);
        // A return value is never sent to the procedure.
        Assert.DoesNotContain("__p_status.Value =", generated, System.StringComparison.Ordinal);
        // Only SQL Server sets a return value; on another provider Value stays
        // null, which the generated code reports instead of reading it as 0.
        Assert.Contains("if (__p_status.Value is null)", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("if (__p_doubled.Value is null)", generated, System.StringComparison.Ordinal);
        // It is read back after the reader is drained, like an output parameter.
        Assert.Contains("var __out_Status = global::System.Convert.ToInt32(__p_status.Value!", generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_doubled.Direction = global::System.Data.ParameterDirection.Output;", generated, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("int?")]
    public void RETURN_VALUE_convention_binds_a_return_value_parameter(string type)
    {
        var generated = Generate($$"""
            [StoredProcedure("usp_X")]
            public partial Task<(OrderRow Row, {{type}} RETURN_VALUE)> RunAsync(
                int seed, {{type}} RETURN_VALUE, CancellationToken ct);
            """);

        Assert.Contains("__p_RETURN_VALUE.Direction = " + ReturnValueDirection, generated, System.StringComparison.Ordinal);
        Assert.Contains("__p_RETURN_VALUE.DbType = global::System.Data.DbType.Int32;", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain("__p_RETURN_VALUE.Value =", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void RETURN_VALUE_convention_applies_to_the_bound_name()
    {
        var generated = Generate("""
            [StoredProcedure("usp_X")]
            public partial Task<(OrderRow Row, int Status)> RunAsync(
                int seed, [Param(Name = "RETURN_VALUE")] int status, CancellationToken ct);
            """);

        Assert.Contains("__p_status.Direction = " + ReturnValueDirection, generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void RETURN_VALUE_convention_works_on_an_output_only_procedure()
    {
        var generated = Generate("""
            [StoredProcedure("usp_X")]
            public partial Task<(int Doubled, int RETURN_VALUE)> RunAsync(
                int seed, int doubled, int RETURN_VALUE, CancellationToken ct);
            """);

        Assert.Contains("__p_RETURN_VALUE.Direction = " + ReturnValueDirection, generated, System.StringComparison.Ordinal);
        Assert.Contains("await __cmd.ExecuteNonQueryAsync(", generated, System.StringComparison.Ordinal);
    }

    // An explicit Direction wins over the name, so a procedure that really declares
    // an `@RETURN_VALUE ... OUTPUT` parameter keeps binding it as one.
    [Theory]
    [InlineData("ParameterDirection.Output", "Output")]
    [InlineData("ParameterDirection.InputOutput", "InputOutput")]
    public void Explicit_direction_on_RETURN_VALUE_overrides_the_convention(string direction, string emitted)
    {
        var generated = Generate($$"""
            [StoredProcedure("usp_X")]
            public partial Task<(OrderRow Row, int RETURN_VALUE)> RunAsync(
                int seed, [Param(Direction = {{direction}})] int RETURN_VALUE, CancellationToken ct);
            """);

        Assert.Contains("__p_RETURN_VALUE.Direction = global::System.Data.ParameterDirection." + emitted + ";", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain(ReturnValueDirection, generated, System.StringComparison.Ordinal);
    }

    // A RETURN value is an int, so a RETURN_VALUE field of another type cannot be
    // one. It compiled in 2.0.0 as an ordinary output parameter and still does.
    [Theory]
    [InlineData("long")]
    [InlineData("string")]
    public void RETURN_VALUE_convention_on_a_non_int_field_stays_an_output_parameter(string type)
    {
        var generated = Generate($$"""
            [StoredProcedure("usp_X")]
            public partial Task<(OrderRow Row, {{type}} RETURN_VALUE)> RunAsync(
                int seed, {{type}} RETURN_VALUE, CancellationToken ct);
            """);

        Assert.Contains("__p_RETURN_VALUE.Direction = global::System.Data.ParameterDirection.Output;", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain(ReturnValueDirection, generated, System.StringComparison.Ordinal);
    }

    // The convention is the exact name SqlClient gives the return value when it
    // derives a procedure's parameters. Another spelling is an ordinary output.
    [Fact]
    public void RETURN_VALUE_convention_is_case_sensitive()
    {
        var generated = Generate("""
            [StoredProcedure("usp_X")]
            public partial Task<(OrderRow Row, int Return_Value)> RunAsync(
                int seed, int return_value, CancellationToken ct);
            """);

        Assert.Contains("__p_return_value.Direction = global::System.Data.ParameterDirection.Output;", generated, System.StringComparison.Ordinal);
        Assert.DoesNotContain(ReturnValueDirection, generated, System.StringComparison.Ordinal);
    }

    // [Query] and [Command] have no output parameters, so a tuple field named
    // RETURN_VALUE there is a result-set position, as before.
    [Fact]
    public void RETURN_VALUE_on_a_Query_is_not_a_return_value()
    {
        var (result, _) = GeneratorHarness.RunGeneratorAndCompile(Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1; SELECT 2")]
                public partial Task<(int A, int RETURN_VALUE)> RunAsync(int RETURN_VALUE, CancellationToken ct);
            }
            """);

        var generated = result.GeneratedTrees
            .Single(t => t.FilePath.EndsWith("Repo.g.cs", System.StringComparison.Ordinal))
            .GetText()
            .ToString();
        Assert.DoesNotContain("ParameterDirection", generated, System.StringComparison.Ordinal);
    }
}
