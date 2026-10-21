using System;
using System.Linq;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using ZeroAlloc.ORM.Generator.Linq;
using ZeroAlloc.ORM.Generator.Model;
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

    // ---- model content ----

    private static CSharpGeneratorDriver CreateDriver()
        => CSharpGeneratorDriver.Create(
            new[] { new ZeroAlloc.ORM.Generator.OrmGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static EntityMapModel[] Maps(GeneratorDriverRunResult result) =>
        ((EntityMapSet)result.Results[0].TrackedSteps["EntityMapSets"][0].Outputs[0].Value).Maps.ToArray();

    // The "EntityMaps" step projects Maps out of "EntityMapSets". Roslyn runs it only when a
    // [LinqQuery] method consumes it, so these tests read the set; LinqIncrementalityTests covers it.

    private static ColumnMapModel Column(EntityMapModel map, string property) =>
        Assert.Single(map.Columns, c => string.Equals(c.Property, property, StringComparison.Ordinal));

    // RunGenerator does not track steps, so the maps come from a tracking driver.
    private static EntityMapModel MapsOfSingle(string source) => Assert.Single(MapsOf(source));

    private static EntityMapModel[] MapsOf(string source)
    {
        var compilation = GeneratorHarness.CreateCompilation(new[] { CSharpSyntaxTree.ParseText(source) });
        return Maps(CreateDriver().RunGenerators(compilation).GetRunResult());
    }

    [Fact]
    public void Defaults_overrides_schema_and_enum_flag_are_read()
    {
        var map = MapsOfSingle(Entity + """
            [TableMap(typeof(TaskEntity), "Tasks", Schema = "app")]
            [ColumnMap(nameof(TaskEntity.Kind), EnumAsString = true)]
            [ColumnMap(nameof(TaskEntity.Title), Column = "title_text")]
            internal static class TaskMap;
            """);

        Assert.Equal("global::TestApp.TaskEntity", map.EntityTypeFullName);
        Assert.Equal("Tasks", map.Table);
        Assert.Equal("app", map.Schema);
        Assert.Equal(new ColumnMapModel("Id", "Id", false), Column(map, "Id"));
        Assert.Equal(new ColumnMapModel("Title", "title_text", false), Column(map, "Title"));
        Assert.Equal(new ColumnMapModel("Kind", "Kind", true), Column(map, "Kind"));
        Assert.Equal(3, map.Columns.Count);
    }

    private static readonly string[] ExpectedOrder = ["Id", "Name", "Hidden", "Own", "MaybeKind"];

    private const string Shapes = """
        using ZeroAlloc.ORM;

        namespace TestApp;

        public enum Kind { A, B }

        public class Base
        {
            public int Id { get; private set; }
            public virtual string Name { get; set; } = "";
            public string Hidden { get; set; } = "";
        }

        public sealed class Derived : Base
        {
            public string Own { get; set; } = "";
            public override string Name { get; set; } = "";
            public new string Hidden { get; set; } = "";
            public Kind? MaybeKind { get; set; }
            public string WriteOnly { set { } }
            public static string Shared { get; set; } = "";
            public string this[int i] => "";
            private string Secret { get; set; } = "";
        }

        """;

    [Fact]
    public void Inherited_properties_come_first_and_hiding_keeps_one_entry()
    {
        var map = MapsOfSingle(Shapes + """
            [TableMap(typeof(Derived), "D")]
            internal static class DMap;
            """);

        Assert.Equal(
            ExpectedOrder,
            map.Columns.Select(c => c.Property).ToArray());
    }

    [Fact]
    public void Nullable_enum_accepts_EnumAsString()
    {
        var source = Shapes + """
            [TableMap(typeof(Derived), "D")]
            [ColumnMap(nameof(Derived.MaybeKind), EnumAsString = true)]
            internal static class DMap;
            """;

        Assert.Empty(Zao106(source));
        Assert.True(Column(MapsOfSingle(source), "MaybeKind").EnumAsString);
    }

    [Fact]
    public void Second_column_map_on_a_property_still_reports_mapped_twice_after_an_enum_error()
    {
        var diagnostics = Zao106(Entity + """
            [TableMap(typeof(TaskEntity), "Tasks")]
            [ColumnMap(nameof(TaskEntity.Title), EnumAsString = true)]
            [ColumnMap(nameof(TaskEntity.Title), Column = "b")]
            internal static class TaskMap;
            """);

        Assert.Equal(2, diagnostics.Length);
        Assert.Contains(diagnostics, d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("twice", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_properties_mapped_to_the_same_column_report_ZAO106()
    {
        var overrideVsOverride = Zao106(Entity + """
            [TableMap(typeof(TaskEntity), "Tasks")]
            [ColumnMap(nameof(TaskEntity.Title), Column = "c")]
            [ColumnMap(nameof(TaskEntity.Kind), Column = "c")]
            internal static class TaskMap;
            """);
        var overrideVsDefault = Zao106(Entity + """
            [TableMap(typeof(TaskEntity), "Tasks")]
            [ColumnMap(nameof(TaskEntity.Title), Column = "Id")]
            internal static class TaskMap;
            """);

        Assert.Single(overrideVsOverride);
        var d = Assert.Single(overrideVsDefault);
        Assert.Contains("column 'Id'", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // ---- ordering across files ----

    [Fact]
    public void Duplicate_table_map_across_files_reports_on_the_later_file_in_path_order()
    {
        var entity = CSharpSyntaxTree.ParseText(Entity, path: "/src/Entity.cs");
        var a = CSharpSyntaxTree.ParseText(
            "using ZeroAlloc.ORM; namespace TestApp; [TableMap(typeof(TaskEntity), \"A\")] internal static class AMap;",
            path: "/src/A.cs");
        var z = CSharpSyntaxTree.ParseText(
            "using ZeroAlloc.ORM; namespace TestApp; [TableMap(typeof(TaskEntity), \"Z\")] internal static class ZMap;",
            path: "/src/Z.cs");

        // Handed over in the opposite order: the path decides, not the compilation order.
        var compilation = GeneratorHarness.CreateCompilation(new[] { z, entity, a });
        var result = CreateDriver().RunGenerators(compilation).GetRunResult();

        var d = Assert.Single(result.Results[0].Diagnostics, x => string.Equals(x.Id, "ZAO106", StringComparison.Ordinal));
        Assert.Same(z, d.Location.SourceTree);
        Assert.Equal("A", Assert.Single(Maps(result)).Table);
    }

    // ---- incrementality ----

    private static readonly string[] MapSteps = ["EntityMapReads", "EntityMapSets"];

    private const string MapSource = """
        using ZeroAlloc.ORM;

        namespace TestApp;

        public sealed class TaskEntity { public int Id { get; set; } }

        [TableMap(typeof(TaskEntity), "Tasks")]
        internal static class TaskMap;
        """;

    private static void AssertCached(GeneratorRunResult run, params string[] steps)
    {
        foreach (var name in steps)
        {
            foreach (var step in run.TrackedSteps[name])
            {
                foreach (var (_, reason) in step.Outputs)
                {
                    Assert.True(
                        reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                        $"Step '{name}' output was {reason}.");
                }
            }
        }
    }

    [Fact]
    public void Unrelated_edit_leaves_the_map_steps_cached()
    {
        var map = CSharpSyntaxTree.ParseText(MapSource, path: "/src/Map.cs");
        var other = CSharpSyntaxTree.ParseText("namespace TestApp; public class Other { }", path: "/src/Other.cs");
        var compilation = GeneratorHarness.CreateCompilation(new[] { map, other });

        var driver = CreateDriver().RunGenerators(compilation);
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(
            other, other.WithChangedText(SourceText.From("namespace TestApp; public class Other { public int X; }"))));

        AssertCached(driver.GetRunResult().Results[0], MapSteps);
    }

    [Fact]
    public void Edit_above_the_map_that_changes_nothing_keeps_the_map_set_unchanged()
    {
        var map = CSharpSyntaxTree.ParseText(MapSource, path: "/src/Map.cs");
        var compilation = GeneratorHarness.CreateCompilation(new[] { map });

        var driver = CreateDriver().RunGenerators(compilation);
        var shifted = map.WithChangedText(SourceText.From("// a comment shifting every position\n\n" + MapSource));
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(map, shifted));

        var run = driver.GetRunResult().Results[0];
        AssertCached(run, "EntityMapSets");
        Assert.Equal("Tasks", Assert.Single(Maps(driver.GetRunResult())).Table);
    }
}
