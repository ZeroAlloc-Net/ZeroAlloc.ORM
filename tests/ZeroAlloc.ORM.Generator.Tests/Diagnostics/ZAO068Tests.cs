using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// #241 — ZAO068 fires when more than one parameter of a method binds as the
// procedure's RETURN value. A procedure has one RETURN value, and SqlClient fills
// only one ReturnValue parameter.
public class ZAO068Tests
{
    private const string Usings = """
        using System;
        using System.Data;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        """;

    private static Diagnostic[] Run(string members)
        => GeneratorHarness.RunGenerator(Usings
                + "public sealed partial class Repo(IAsyncDbConnection connection)\n{\n"
                + members + "\n}\n")
            .Diagnostics
            .Where(d => string.Equals(d.Id, "ZAO068", System.StringComparison.Ordinal))
            .ToArray();

    [Fact]
    public void Two_explicit_ReturnValue_parameters_report_ZAO068()
    {
        var diagnostics = Run("""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, int First, int Second)> RunAsync(
                int id,
                [Param(Direction = ParameterDirection.ReturnValue)] int first,
                [Param(Direction = ParameterDirection.ReturnValue)] int second,
                CancellationToken ct);
            """);

        var zao068 = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, zao068.Severity);
        var message = zao068.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("'second'", message, System.StringComparison.Ordinal);
        Assert.Contains("'first'", message, System.StringComparison.Ordinal);
        Assert.Contains("'RunAsync'", message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_ReturnValue_and_the_RETURN_VALUE_convention_report_ZAO068()
    {
        var diagnostics = Run("""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, int Status, int RETURN_VALUE)> RunAsync(
                int id,
                [Param(Direction = ParameterDirection.ReturnValue)] int status,
                int RETURN_VALUE,
                CancellationToken ct);
            """);

        Assert.Single(diagnostics);
    }

    [Fact]
    public void One_ReturnValue_beside_output_parameters_reports_no_ZAO068()
    {
        Assert.Empty(Run("""
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, int Doubled, int RETURN_VALUE)> RunAsync(
                int id, int doubled, int RETURN_VALUE, CancellationToken ct);
            """));
    }
}
