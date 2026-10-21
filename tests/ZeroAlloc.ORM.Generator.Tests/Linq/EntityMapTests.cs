using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// ZAO106 reports an invalid entity map: a TableMap/ColumnMap pair the LINQ translator
// cannot rely on. Each case anchors at the offending attribute.
public class EntityMapTests
{
    private const string Entity = """
        using ZeroAlloc.ORM;

        namespace TestApp;

        public enum Kind { A, B }

        public sealed class TaskEntity
        {
            public int Id { get; private set; }
            public string Title { get; set; } = "";
            public Kind Kind { get; set; }
        }

        """;

    private static Diagnostic[] Zao106(string source) =>
        GeneratorHarness.RunGenerator(source).Results[0].Diagnostics
            .Where(d => string.Equals(d.Id, "ZAO106", StringComparison.Ordinal))
            .ToArray();

    [Fact]
    public void Valid_map_reports_nothing()
    {
        var diagnostics = Zao106(Entity + """
            [TableMap(typeof(TaskEntity), "Tasks", Schema = "app")]
            [ColumnMap(nameof(TaskEntity.Kind), EnumAsString = true)]
            [ColumnMap(nameof(TaskEntity.Title), Column = "title_text")]
            internal static class TaskMap;
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Two_table_maps_for_the_same_entity_report_on_the_second_in_file_order()
    {
        var diagnostics = Zao106(Entity + """
            [TableMap(typeof(TaskEntity), "Tasks")]
            internal static class FirstMap;

            [TableMap(typeof(TaskEntity), "Tasks2")]
            internal static class SecondMap;
            """);

        var d = Assert.Single(diagnostics);
        Assert.Contains("TaskEntity", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var text = d.Location.SourceTree!.ToString();
        Assert.Contains("\"Tasks2\"", text.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length), StringComparison.Ordinal);
    }

    [Fact]
    public void Column_map_for_an_unknown_property_reports_ZAO106()
    {
        var diagnostics = Zao106(Entity + """
            [TableMap(typeof(TaskEntity), "Tasks")]
            [ColumnMap("Nope", Column = "x")]
            internal static class TaskMap;
            """);

        var d = Assert.Single(diagnostics);
        Assert.Contains("Nope", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Same_property_mapped_twice_reports_ZAO106_on_the_second()
    {
        var diagnostics = Zao106(Entity + """
            [TableMap(typeof(TaskEntity), "Tasks")]
            [ColumnMap(nameof(TaskEntity.Title), Column = "a")]
            [ColumnMap(nameof(TaskEntity.Title), Column = "b")]
            internal static class TaskMap;
            """);

        var d = Assert.Single(diagnostics);
        var text = d.Location.SourceTree!.ToString();
        Assert.Contains("\"b\"", text.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length), StringComparison.Ordinal);
    }

    [Fact]
    public void EnumAsString_on_a_non_enum_property_reports_ZAO106()
    {
        var diagnostics = Zao106(Entity + """
            [TableMap(typeof(TaskEntity), "Tasks")]
            [ColumnMap(nameof(TaskEntity.Title), EnumAsString = true)]
            internal static class TaskMap;
            """);

        var d = Assert.Single(diagnostics);
        Assert.Contains("Title", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
