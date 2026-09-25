using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// #241 — ZAO067 fires when `[Param(Direction = ParameterDirection.ReturnValue)]`
// reads the procedure's RETURN value into a tuple field that is not `int` or
// `int?`. SQL Server's RETURN value is always an int.
//
// Only the explicit Direction is checked. A field named RETURN_VALUE of another
// type compiled in 2.0.0 as an ordinary output parameter, so the convention
// leaves it one instead of reporting it.
public class ZAO067Tests
{
    private const string Usings = """
        using System;
        using System.Data;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public enum Status { Open = 1, Closed = 2 }

        """;

    private static Diagnostic[] Run(string members)
        => GeneratorHarness.RunGenerator(Usings
                + "public sealed partial class Repo(IAsyncDbConnection connection)\n{\n"
                + members + "\n}\n")
            .Diagnostics
            .Where(d => string.Equals(d.Id, "ZAO067", System.StringComparison.Ordinal))
            .ToArray();

    [Theory]
    [InlineData("long")]
    [InlineData("short")]
    [InlineData("decimal")]
    [InlineData("string")]
    [InlineData("Status")]
    public void ReturnValue_into_a_non_int_field_reports_ZAO067(string type)
    {
        var diagnostics = Run($$"""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, {{type}} Status)> RunAsync(
                int id, [Param(Direction = ParameterDirection.ReturnValue)] {{type}} status, CancellationToken ct);
            """);

        var zao067 = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, zao067.Severity);
        var message = zao067.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("'status'", message, System.StringComparison.Ordinal);
        Assert.Contains("'RunAsync'", message, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("int?")]
    public void ReturnValue_into_an_int_field_reports_no_ZAO067(string type)
    {
        Assert.Empty(Run($$"""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, {{type}} Status)> RunAsync(
                int id, [Param(Direction = ParameterDirection.ReturnValue)] {{type}} status, CancellationToken ct);
            """));
    }

    [Fact]
    public void RETURN_VALUE_convention_on_a_non_int_field_reports_no_ZAO067()
    {
        Assert.Empty(Run("""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, long RETURN_VALUE)> RunAsync(
                int id, long RETURN_VALUE, CancellationToken ct);
            """));
    }
}
