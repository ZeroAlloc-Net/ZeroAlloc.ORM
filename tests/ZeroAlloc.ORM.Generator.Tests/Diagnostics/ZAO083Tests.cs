#nullable enable
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// ZAO083 fires when [Json] marks something that is not a string. [Json] means a JSON
// document held as a string, so any other type, a string[] included, is a mistake.
// Like ZAO042 it runs in its own pipeline, so it fires wherever the marker is
// written, whether or not a [Query] or [Command] method uses it.
public class ZAO083Tests
{
    private const string Usings = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        """;

    [Fact]
    public void Json_on_int_reports_ZAO083()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE posts SET views = @views")]
                public partial Task<int> UpdateAsync([Json] int views, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source, NpgsqlStub.Source);

        var zao083 = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO083", zao083.Id);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, zao083.Severity);
        Assert.Equal(
            "'views' carries [Json] but is of type 'int'. [Json] marks a JSON document held as a string; "
            + "use string or string?.",
            zao083.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        // The squiggle lands on the attribute itself.
        var span = zao083.Location.SourceSpan;
        Assert.Equal("Json", zao083.Location.SourceTree!.ToString().Substring(span.Start, span.Length));
    }

    public static TheoryData<string, string, string> NonStringSources => new()
    {
        { "public sealed record DocRow(Guid Id, [Json] string[] Docs);", "Docs", "string[]" },
        { "public sealed class DocRow { [Json] public Guid? Doc { get; set; } }", "Doc", "System.Guid?" },
        { "public readonly record struct Payload(string Name, [Json] List<string> Docs);", "Docs", "System.Collections.Generic.List<string>" },
        // `property:` puts the marker on the synthesized property, not the parameter.
        { "public sealed record DocRow(Guid Id, [property: Json] string[] Docs);", "Docs", "string[]" },
    };

    // Rows, composites and unused types: the marker on any non-string reports.
    [Theory]
    [MemberData(nameof(NonStringSources))]
    public void Json_on_non_string_member_reports_ZAO083(string declaration, string name, string type)
    {
        var result = GeneratorHarness.RunGenerator(Usings + declaration, NpgsqlStub.Source);

        var zao083 = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO083", zao083.Id);
        Assert.Equal(
            $"'{name}' carries [Json] but is of type '{type}'. [Json] marks a JSON document held as a string; "
            + "use string or string?.",
            zao083.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        var span = zao083.Location.SourceSpan;
        Assert.Equal("Json", zao083.Location.SourceTree!.ToString().Substring(span.Start, span.Length));
    }

    [Fact]
    public void Json_on_string_reports_nothing()
    {
        var source = Usings + """
            public sealed record DocRow(Guid Id, [Json] string Doc, [Json] string? Meta, [property: Json] string Extra);
            public sealed class Other { [Json] public string? Doc { get; set; } }
            """;
        var result = GeneratorHarness.RunGenerator(source, NpgsqlStub.Source);

        Assert.Empty(result.Results[0].Diagnostics);
    }
}
