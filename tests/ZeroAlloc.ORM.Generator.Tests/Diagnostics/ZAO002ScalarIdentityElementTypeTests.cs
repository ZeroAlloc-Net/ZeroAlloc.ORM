using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// #280 — [Command(Kind = Scalar)] and [Command(Kind = Identity)] methods whose
// return type is already Task<T>/ValueTask<T> (or a bare Task/ValueTask, or a
// nullable T that Identity rejects) previously reused ZAO002_BadReturnType's
// "Expected Task<T>, ValueTask<T>, ..." message. That told the adopter to do
// what they already did. This branch (OrmGenerator.TransformMethod, the
// ZAO002-for-Scalar/Identity block right after ClassifyEmitShape) now reports
// through ZAO002_UnsupportedScalarOrIdentityType — same ID, category and
// severity, a message that names the actual problem: T has no reader for the
// command kind, T is nullable on a kind that forbids it, or the return type
// isn't Task<T>/ValueTask<T> at all.
public class ZAO002ScalarIdentityElementTypeTests
{
    private const string Usings = """
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        """;

    [Fact]
    public void Scalar_command_with_unsupported_element_type_names_the_type()
    {
        var source = Usings + """
            public sealed class Widget
            {
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("SELECT W FROM Widgets", Kind = CommandKind.Scalar)]
                public partial Task<Widget> GetWidgetAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        var zao002 = Assert.Single(diagnostics, d => string.Equals(d.Id, "ZAO002", System.StringComparison.Ordinal));
        Assert.Equal(DiagnosticSeverity.Error, zao002.Severity);
        var message = zao002.GetMessage(CultureInfo.InvariantCulture);
        Assert.Equal(
            "Method 'GetWidgetAsync' has return type 'System.Threading.Tasks.Task<TestApp.Widget>'. "
            + "[Command(Kind = Scalar)] cannot use this return type: 'TestApp.Widget' has no scalar reader; "
            + "supported types are int, long, short, byte, bool, decimal, double, float, string, DateTime, "
            + "DateTimeOffset, TimeSpan, DateOnly, TimeOnly, Guid, or byte[] (also an enum, or a value object "
            + "or factory wrapping one of those).",
            message,
            ignoreCase: false);

        // Reported on the return-type syntax, not the method identifier.
        var span = zao002.Location.SourceSpan;
        Assert.Equal("Task<Widget>", source.Substring(span.Start, span.Length));
    }

    [Fact]
    public void Identity_command_with_unsupported_primitive_names_the_type()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (...) VALUES (...) RETURNING Total", Kind = CommandKind.Identity)]
                public partial Task<decimal> InsertAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        var zao002 = Assert.Single(diagnostics, d => string.Equals(d.Id, "ZAO002", System.StringComparison.Ordinal));
        var message = zao002.GetMessage(CultureInfo.InvariantCulture);
        Assert.Equal(
            "Method 'InsertAsync' has return type 'System.Threading.Tasks.Task<decimal>'. "
            + "[Command(Kind = Identity)] cannot use this return type: 'decimal' has no identity reader; "
            + "supported types are int, long, or Guid, always non-null (or a value object or factory "
            + "wrapping one of those).",
            message,
            ignoreCase: false);

        var span = zao002.Location.SourceSpan;
        Assert.Equal("Task<decimal>", source.Substring(span.Start, span.Length));
    }

    [Fact]
    public void Identity_command_with_nullable_result_type_rejects_nullability_by_name()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (...) VALUES (...) RETURNING Id", Kind = CommandKind.Identity)]
                public partial Task<int?> InsertAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        var zao002 = Assert.Single(diagnostics, d => string.Equals(d.Id, "ZAO002", System.StringComparison.Ordinal));
        var message = zao002.GetMessage(CultureInfo.InvariantCulture);
        Assert.Equal(
            "Method 'InsertAsync' has return type 'System.Threading.Tasks.Task<int?>'. "
            + "[Command(Kind = Identity)] cannot use this return type: the result cannot be nullable; "
            + "supported types are int, long, or Guid, always non-null (or a value object or factory "
            + "wrapping one of those).",
            message,
            ignoreCase: false);

        var span = zao002.Location.SourceSpan;
        Assert.Equal("Task<int?>", source.Substring(span.Start, span.Length));
    }

    [Fact]
    public void Scalar_command_returning_bare_Task_reports_the_missing_value()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("SELECT 1", Kind = CommandKind.Scalar)]
                public partial Task RunAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        var zao002 = Assert.Single(diagnostics, d => string.Equals(d.Id, "ZAO002", System.StringComparison.Ordinal));
        var message = zao002.GetMessage(CultureInfo.InvariantCulture);
        Assert.Equal(
            "Method 'RunAsync' has return type 'System.Threading.Tasks.Task'. "
            + "[Command(Kind = Scalar)] cannot use this return type: a scalar command must return Task<T> "
            + "or ValueTask<T> carrying a value; supported types are int, long, short, byte, bool, decimal, "
            + "double, float, string, DateTime, DateTimeOffset, TimeSpan, DateOnly, TimeOnly, Guid, or byte[] "
            + "(also an enum, or a value object or factory wrapping one of those).",
            message,
            ignoreCase: false);

        var span = zao002.Location.SourceSpan;
        Assert.Equal("Task", source.Substring(span.Start, span.Length));
    }
}
