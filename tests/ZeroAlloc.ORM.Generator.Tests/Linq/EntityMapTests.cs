using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using ZeroAlloc.ORM.Generator.Linq;
using ZeroAlloc.ORM.Generator.Model;

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

    // Only properties a column can stand behind are mapped: one with a setter of any
    // accessibility, init included, or a get-only auto-property, which has a backing field.
    // A computed get-only property is skipped. An override counts its base's setter.
    [Fact]
    public void Computed_get_only_properties_are_not_columns()
    {
        var map = MapsOfSingle("""
            using ZeroAlloc.ORM;

            namespace TestApp;

            public abstract class Base
            {
                public virtual string Name { get; set; } = "";
            }

            public sealed class Computed : Base
            {
                public int Id { get; private set; }
                public string First { get; init; } = "";
                public string Created { get; } = "";
                public string Label => First + "!";
                public string Upper { get { return First.ToUpperInvariant(); } }
                public string Proxy { get => First; set { } }
                public override string Name => "fixed";
            }

            [TableMap(typeof(Computed), "C")]
            internal static class CMap;
            """);

        Assert.Equal(
            ["Name", "Id", "First", "Created", "Proxy"],
            map.Columns.Select(c => c.Property).ToArray());
    }

    // ---- entities from a referenced assembly ----

    // Metadata does not import a private setter, so Note, which has one, and the computed Label
    // both look get-only there. A get-only property counts only when its getter is compiler
    // generated, that is an auto-property: Id and Created are columns, Note and Label are not.
    private const string Remote = """
        namespace Lib;

        public enum Kind { A, B }

        public sealed class Remote
        {
            private string _note = "";
            public int Id { get; private set; }
            public string Created { get; } = "";
            public string Title { get; set; } = "";
            public Kind Kind { get; init; }
            public string Note { get => _note; private set => _note = value; }
            public string Label => Created + "!";
        }
        """;

    private static EntityMapModel RemoteMap(bool referenceAssembly, string map) =>
        Assert.Single(Maps(CreateDriver().RunGenerators(RemoteCompilation(referenceAssembly, map)).GetRunResult()));

    private static Diagnostic[] RemoteZao106(bool referenceAssembly, string map) =>
        CreateDriver().RunGenerators(RemoteCompilation(referenceAssembly, map)).GetRunResult().Results[0].Diagnostics
            .Where(d => string.Equals(d.Id, "ZAO106", StringComparison.Ordinal))
            .ToArray();

    private static CSharpCompilation RemoteCompilation(bool referenceAssembly, string source) =>
        GeneratorHarness.CreateCompilation(new[] { CSharpSyntaxTree.ParseText(source) })
            .AddReferences(GeneratorHarness.CompileReference(Remote, "Lib", referenceAssembly));

    private const string RemoteMapHead = """
        using ZeroAlloc.ORM;

        namespace TestApp;

        [TableMap(typeof(Lib.Remote), "R")]

        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Referenced_entity_maps_auto_properties_and_skips_computed_ones(bool referenceAssembly)
    {
        var map = RemoteMap(referenceAssembly, RemoteMapHead + "internal static class RMap;");

        Assert.Equal(["Id", "Created", "Title", "Kind"], map.Columns.Select(c => c.Property).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Column_map_forces_a_referenced_property_in(bool referenceAssembly)
    {
        var source = RemoteMapHead + """
            [ColumnMap(nameof(Lib.Remote.Note))]
            [ColumnMap(nameof(Lib.Remote.Label), Column = "label")]
            internal static class RMap;
            """;

        Assert.Empty(RemoteZao106(referenceAssembly, source));
        Assert.Equal(
            ["Id", "Created", "Title", "Kind", "Note", "Label"],
            RemoteMap(referenceAssembly, source).Columns.Select(c => c.Property).ToArray());
        Assert.Equal("label", Column(RemoteMap(referenceAssembly, source), "Label").Column);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ignore_drops_a_referenced_auto_property(bool referenceAssembly)
    {
        var source = RemoteMapHead + """
            [ColumnMap(nameof(Lib.Remote.Created), Ignore = true)]
            internal static class RMap;
            """;

        Assert.Empty(RemoteZao106(referenceAssembly, source));
        Assert.Equal(
            ["Id", "Title", "Kind"],
            RemoteMap(referenceAssembly, source).Columns.Select(c => c.Property).ToArray());
    }

    [Theory]
    [InlineData(false, "Column = \"c\"")]
    [InlineData(true, "Column = \"c\"")]
    [InlineData(false, "EnumAsString = true")]
    [InlineData(true, "EnumAsString = true")]
    public void Ignore_with_Column_or_EnumAsString_on_a_referenced_property_reports_ZAO106(
        bool referenceAssembly, string setting)
    {
        var diagnostics = RemoteZao106(referenceAssembly, RemoteMapHead + $$"""
            [ColumnMap(nameof(Lib.Remote.Kind), Ignore = true, {{setting}})]
            internal static class RMap;
            """);

        var d = Assert.Single(diagnostics);
        Assert.Contains("Ignore", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var text = d.Location.SourceTree!.ToString();
        Assert.Contains("Ignore = true", text.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length), StringComparison.Ordinal);
    }

    // A shared kernel in a referenced assembly: the DDD base every entity derives from, and a
    // virtual property overridden there by a computed one.
    private const string Kernel = """
        namespace Kernel;

        public abstract class Entity
        {
            public int Id { get; private set; }
        }

        public class Named : Entity
        {
            public virtual string Name { get; set; } = "";
        }

        public sealed class Fixed : Named
        {
            public override string Name => "x";
        }
        """;

    private static EntityMapModel KernelMap(bool referenceAssembly, string source)
    {
        var compilation = GeneratorHarness.CreateCompilation(new[] { CSharpSyntaxTree.ParseText(source) })
            .AddReferences(GeneratorHarness.CompileReference(Kernel, "Kernel", referenceAssembly));
        return Assert.Single(Maps(CreateDriver().RunGenerators(compilation).GetRunResult()));
    }

    // An override counts its base's setter in metadata too, as it does in source.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Referenced_override_of_a_settable_property_is_a_column(bool referenceAssembly)
    {
        var map = KernelMap(referenceAssembly, """
            using ZeroAlloc.ORM;

            namespace TestApp;

            [TableMap(typeof(Kernel.Fixed), "F")]
            internal static class FMap;
            """);

        Assert.Equal(["Id", "Name"], map.Columns.Select(c => c.Property).ToArray());
    }

    // The common DDD shape: an entity in this compilation derives from a referenced base whose Id
    // has a private setter. Id is a column, since its getter is compiler generated.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entity_deriving_from_a_referenced_base_maps_the_base_auto_property(bool referenceAssembly)
    {
        var map = KernelMap(referenceAssembly, """
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed class Order : Kernel.Entity
            {
                public string Number { get; private set; } = "";
                public string Label => Number + "!";
            }

            [TableMap(typeof(Order), "Orders")]
            internal static class OrderMap;
            """);

        Assert.Equal(["Id", "Number"], map.Columns.Select(c => c.Property).ToArray());
    }

    // A query that uses a property the map leaves out fails at build time with ZAO105, not at run
    // time; [ColumnMap] on it brings it in.
    [Theory]
    [InlineData(false, "", "Label", "ZAO105")]
    [InlineData(true, "", "Label", "ZAO105")]
    [InlineData(true, "", "Note", "ZAO105")]
    [InlineData(true, "[ColumnMap(nameof(Lib.Remote.Note))]", "Note", null)]
    [InlineData(true, "[ColumnMap(nameof(Lib.Remote.Created), Ignore = true)]", "Created", "ZAO105")]
    public void Query_on_a_property_the_map_leaves_out_reports_ZAO105(
        bool referenceAssembly, string columnMap, string member, string? expected)
    {
        var source = $$"""
            #nullable enable
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;
            using ZeroAlloc.ORM.Linq;

            namespace TestApp;

            [TableMap(typeof(Lib.Remote), "R")]
            {{columnMap}}
            internal static class RMap;

            public sealed record Row(int Id, string Text);

            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class Store(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(RowsQuery))]
                public partial Task<List<Row>> Rows(CancellationToken ct);

                private static IQuery<Row> RowsQuery() =>
                    Sql.Table<Lib.Remote>().Select(r => new Row(r.Id, r.{{member}}));
            }
            """;

        var result = CreateDriver().RunGenerators(RemoteCompilation(referenceAssembly, source)).GetRunResult();
        var diagnostics = result.Results[0].Diagnostics;

        if (expected is null)
        {
            Assert.Empty(diagnostics);
            // The SQL sits in a C# string literal, so its quotes are escaped.
            var generated = Assert.Single(result.Results[0].GeneratedSources).SourceText.ToString();
            Assert.Contains("SELECT \\\"Id\\\", \\\"Note\\\" FROM \\\"R\\\"", generated, StringComparison.Ordinal);
        }
        else
        {
            var d = Assert.Single(diagnostics);
            Assert.Equal(expected, d.Id);
            var text = d.Location.SourceTree!.ToString();
            Assert.Equal("r." + member, text.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length));
        }
    }

    // ---- overrides on a same-compilation entity ----

    // [ColumnMap] names a property the map should store, so it brings a computed one in too.
    [Fact]
    public void Column_map_forces_a_computed_property_in()
    {
        var source = Entity + """
            public sealed class WithLabel
            {
                public int Id { get; private set; }
                public string Label => "x";
            }

            [TableMap(typeof(WithLabel), "L")]
            [ColumnMap(nameof(WithLabel.Label), Column = "label")]
            internal static class LabelMap;
            """;

        Assert.Empty(Zao106(source));
        var map = MapsOfSingle(source);
        Assert.Equal(["Id", "Label"], map.Columns.Select(c => c.Property).ToArray());
        Assert.Equal("label", Column(map, "Label").Column);
    }

    [Fact]
    public void Ignore_drops_a_property()
    {
        var source = Entity + """
            [TableMap(typeof(TaskEntity), "Tasks")]
            [ColumnMap(nameof(TaskEntity.Title), Ignore = true)]
            internal static class TaskMap;
            """;

        Assert.Empty(Zao106(source));
        Assert.Equal(["Id", "Kind"], MapsOfSingle(source).Columns.Select(c => c.Property).ToArray());
    }

    // An ignored property holds no column, so another may take its name.
    [Fact]
    public void Ignored_property_does_not_claim_its_column()
    {
        var source = Entity + """
            [TableMap(typeof(TaskEntity), "Tasks")]
            [ColumnMap(nameof(TaskEntity.Title), Ignore = true)]
            [ColumnMap(nameof(TaskEntity.Kind), Column = "Title")]
            internal static class TaskMap;
            """;

        Assert.Empty(Zao106(source));
        Assert.Equal("Title", Column(MapsOfSingle(source), "Kind").Column);
    }

    [Fact]
    public void Ignore_with_EnumAsString_reports_ZAO106()
    {
        var d = Assert.Single(Zao106(Entity + """
            [TableMap(typeof(TaskEntity), "Tasks")]
            [ColumnMap(nameof(TaskEntity.Kind), Ignore = true, EnumAsString = true)]
            internal static class TaskMap;
            """));

        Assert.Contains("Kind", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
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

    // ---- DateTime storage ----

    private const string TimesUsings = """
        using System;
        using ZeroAlloc.ORM;

        """;

    private const string TimesBody = """
        namespace TestApp;

        public sealed class Execution
        {
            public int Id { get; private set; }
            public DateTime ExecutedAt { get; private set; }
            public DateTime StartedAt { get; private set; }
            public DateTime? FinishedAt { get; private set; }
            public DateTimeOffset LoggedAt { get; private set; }
        }

        """;

    private const string Times = TimesUsings + TimesBody;

    private const string TimesWithTzDefault = TimesUsings + """
        [assembly: EntityMapDefaults(DateTimeStorage = DateTimeStorage.TimestampTz)]

        """ + TimesBody;

    // A column's own setting wins; a DateTime column without one, nullable or not, takes the
    // assembly default; a column that is not a DateTime has no DateTime storage.
    [Fact]
    public void Effective_DateTime_storage_is_the_column_setting_else_the_assembly_default()
    {
        var source = TimesWithTzDefault + """
            [TableMap(typeof(Execution), "Executions")]
            [ColumnMap(nameof(Execution.StartedAt), DateTimeStorage = DateTimeStorage.Timestamp)]
            internal static class ExecutionMap;
            """;

        Assert.Empty(Zao106(source));
        var map = MapsOfSingle(source);
        Assert.Equal(DateTimeColumnStorage.TimestampTz, Column(map, "ExecutedAt").DateTimeStorage);
        Assert.Equal(DateTimeColumnStorage.Timestamp, Column(map, "StartedAt").DateTimeStorage);
        Assert.Equal(DateTimeColumnStorage.TimestampTz, Column(map, "FinishedAt").DateTimeStorage);
        Assert.Equal(DateTimeColumnStorage.None, Column(map, "LoggedAt").DateTimeStorage);
        Assert.Equal(DateTimeColumnStorage.None, Column(map, "Id").DateTimeStorage);
    }

    [Fact]
    public void DateTime_storage_without_assembly_default_is_the_column_setting_else_Default()
    {
        var map = MapsOfSingle(Times + """
            [TableMap(typeof(Execution), "Executions")]
            [ColumnMap(nameof(Execution.FinishedAt), DateTimeStorage = DateTimeStorage.TimestampTz)]
            internal static class ExecutionMap;
            """);

        Assert.Equal(DateTimeColumnStorage.Default, Column(map, "ExecutedAt").DateTimeStorage);
        Assert.Equal(DateTimeColumnStorage.TimestampTz, Column(map, "FinishedAt").DateTimeStorage);
        Assert.Equal(new ColumnMapModel("Id", "Id", false), Column(map, "Id"));
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("LoggedAt")]
    public void DateTimeStorage_on_a_property_that_is_not_a_DateTime_reports_ZAO106(string property)
    {
        var d = Assert.Single(Zao106(Times + """
            [TableMap(typeof(Execution), "Executions")]
            [ColumnMap(
            """ + "\"" + property + "\"" + """
            , DateTimeStorage = DateTimeStorage.TimestampTz)]
            internal static class ExecutionMap;
            """));

        var message = d.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(property, message, StringComparison.Ordinal);
        Assert.Contains("DateTimeStorage", message, StringComparison.Ordinal);
        var text = d.Location.SourceTree!.ToString();
        Assert.StartsWith("ColumnMap(", text.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length), StringComparison.Ordinal);
    }

    [Fact]
    public void Default_DateTimeStorage_on_a_property_that_is_not_a_DateTime_is_allowed()
    {
        Assert.Empty(Zao106(Times + """
            [TableMap(typeof(Execution), "Executions")]
            [ColumnMap(nameof(Execution.Id), DateTimeStorage = DateTimeStorage.Default)]
            internal static class ExecutionMap;
            """));
    }

    // A value the enum does not declare is a mistake whatever the property's type.
    [Theory]
    [InlineData("ExecutedAt")]
    [InlineData("Id")]
    public void Undeclared_DateTimeStorage_value_reports_ZAO106(string property)
    {
        var d = Assert.Single(Zao106(Times + """
            [TableMap(typeof(Execution), "Executions")]
            [ColumnMap(
            """ + "\"" + property + "\"" + """
            , DateTimeStorage = (DateTimeStorage)7)]
            internal static class ExecutionMap;
            """));

        var message = d.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(property, message, StringComparison.Ordinal);
        Assert.Contains("7", message, StringComparison.Ordinal);
        var text = d.Location.SourceTree!.ToString();
        Assert.StartsWith("ColumnMap(", text.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length), StringComparison.Ordinal);
    }

    [Fact]
    public void Ignore_with_DateTimeStorage_reports_ZAO106()
    {
        var d = Assert.Single(Zao106(Times + """
            [TableMap(typeof(Execution), "Executions")]
            [ColumnMap(nameof(Execution.ExecutedAt), Ignore = true, DateTimeStorage = DateTimeStorage.TimestampTz)]
            internal static class ExecutionMap;
            """));

        var message = d.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("ExecutedAt", message, StringComparison.Ordinal);
        Assert.Contains("DateTimeStorage", message, StringComparison.Ordinal);
    }

    // ---- owned types ----

    // Daedalus' CodeAnalysisRequest in miniature: two owned objects, a value object, a
    // collection, an enum and a primitive beside them.
    private const string Owned = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public enum Platform { GitHub, GitLab }

        public readonly record struct ProjectId(Guid Value);

        public sealed class RepositoryReference
        {
            public string Url { get; private set; } = "";
            public string? Branch { get; private set; }
            public Platform Platform { get; private set; }
            public DateTime? PushedAt { get; private set; }
            public string Display => Url + "!";
        }

        public sealed class CodeLocation
        {
            public string FilePath { get; private set; } = "";
            public int? StartLine { get; private set; }
            public int? EndLine { get; private set; }
        }

        public sealed class Request
        {
            public Guid Id { get; private set; }
            public RepositoryReference Repository { get; private set; } = null!;
            public CodeLocation Location { get; private set; } = null!;
            public string Title { get; private set; } = "";
            public ProjectId Project { get; private set; }
            public List<CodeLocation> History { get; private set; } = [];
            public Platform Platform { get; private set; }
        }

        """;

    private static ColumnMapModel At(EntityMapModel map, string path) =>
        Assert.Single(map.Columns, c => string.Equals(c.Path, path, StringComparison.Ordinal));

    private static readonly string[] OwnedOrder =
    [
        "Id", "Repository.Url", "Repository.Branch", "Repository.Platform", "Repository.PushedAt",
        "Repository.Display", "Location.FilePath", "Location.StartLine", "Title", "Project", "History", "Platform",
    ];

    // Each owned member is a column named {Owner}_{Property}, at the owned property's position; a
    // dotted ColumnMap renames it or sets its storage, forces a computed member in, or leaves one out.
    [Fact]
    public void Owned_members_map_to_owner_property_columns_with_dotted_overrides()
    {
        var source = Owned + """
            [TableMap(typeof(Request), "Requests")]
            [OwnedMap(nameof(Request.Repository))]
            [OwnedMap(nameof(Request.Location))]
            [ColumnMap("Repository.Url", Column = "RepositoryUrl")]
            [ColumnMap("Repository.Platform", EnumAsString = true)]
            [ColumnMap("Repository.PushedAt", DateTimeStorage = DateTimeStorage.TimestampTz)]
            [ColumnMap("Repository.Display")]
            [ColumnMap("Location.EndLine", Ignore = true)]
            internal static class RequestMap;
            """;

        Assert.Empty(Zao106(source));
        var map = MapsOfSingle(source);
        Assert.Equal(OwnedOrder, map.Columns.Select(c => c.Path).ToArray());
        Assert.Equal(new ColumnMapModel("Url", "RepositoryUrl", false, DateTimeColumnStorage.None, "Repository"), At(map, "Repository.Url"));
        Assert.Equal(new ColumnMapModel("Branch", "Repository_Branch", false, DateTimeColumnStorage.None, "Repository"), At(map, "Repository.Branch"));
        Assert.Equal(new ColumnMapModel("Platform", "Repository_Platform", true, DateTimeColumnStorage.None, "Repository"), At(map, "Repository.Platform"));
        Assert.Equal(new ColumnMapModel("PushedAt", "Repository_PushedAt", false, DateTimeColumnStorage.TimestampTz, "Repository"), At(map, "Repository.PushedAt"));
        Assert.Equal(new ColumnMapModel("FilePath", "Location_FilePath", false, DateTimeColumnStorage.None, "Location"), At(map, "Location.FilePath"));
        Assert.Equal(new ColumnMapModel("Platform", "Platform", false), At(map, "Platform"));
    }

    // An owned DateTime member that declares no storage takes the assembly default, as a plain
    // column does.
    [Fact]
    public void Owned_DateTime_member_takes_the_assembly_default()
    {
        var withDefault = Owned.Replace(
            "using ZeroAlloc.ORM;\n",
            "using ZeroAlloc.ORM;\n\n[assembly: EntityMapDefaults(DateTimeStorage = DateTimeStorage.Timestamp)]\n",
            StringComparison.Ordinal);
        Assert.NotEqual(Owned, withDefault, StringComparer.Ordinal);
        var map = MapsOfSingle(withDefault + """
            [TableMap(typeof(Request), "Requests")]
            [OwnedMap(nameof(Request.Repository))]
            internal static class RequestMap;
            """);

        Assert.Equal(DateTimeColumnStorage.Timestamp, At(map, "Repository.PushedAt").DateTimeStorage);
    }

    // Each invalid owned declaration reports one ZAO106, at the attribute, naming the property or
    // path. A dotted ColumnMap on an owner whose [OwnedMap] is already reported adds no second
    // error, and an owned object whose members are all ignored has no column, so it is invalid.
    [Theory]
    [InlineData("[OwnedMap(\"Nope\")]", "Nope")]
    [InlineData("[OwnedMap(nameof(Request.Title))]", "Title")]
    [InlineData("[OwnedMap(nameof(Request.Project))]", "Project")]
    [InlineData("[OwnedMap(nameof(Request.History))]", "History")]
    [InlineData("[OwnedMap(nameof(Request.Platform))]", "Platform")]
    [InlineData("[OwnedMap(nameof(Request.Location))]\n[OwnedMap(nameof(Request.Location))]", "Location")]
    [InlineData("[ColumnMap(\"Location.FilePath\", Column = \"x\")]", "Location.FilePath")]
    [InlineData("[OwnedMap(nameof(Request.Location))]\n[ColumnMap(\"Location.Nope\", Column = \"x\")]", "Location.Nope")]
    [InlineData("[OwnedMap(nameof(Request.Location))]\n[ColumnMap(\"Location.FilePath.Length\", Column = \"x\")]", "Location.FilePath.Length")]
    [InlineData("[OwnedMap(nameof(Request.Location))]\n[ColumnMap(nameof(Request.Location), Column = \"x\")]", "Location")]
    [InlineData("[OwnedMap(nameof(Request.Location))]\n[ColumnMap(\"Location.FilePath\", Column = \"Title\")]", "Title")]
    [InlineData("[OwnedMap(nameof(Request.Location))]\n[ColumnMap(\"Location.FilePath\", Column = \"a\")]\n[ColumnMap(\"Location.FilePath\", Column = \"b\")]", "Location.FilePath")]
    [InlineData("[OwnedMap(nameof(Request.Title))]\n[ColumnMap(\"Title.Length\", Column = \"x\")]", "Title")]
    [InlineData("[OwnedMap(\"Nope\")]\n[ColumnMap(\"Nope.Member\", Column = \"x\")]", "Nope")]
    [InlineData("[OwnedMap(nameof(Request.Location))]\n[ColumnMap(\"Location.FilePath\", Ignore = true)]\n[ColumnMap(\"Location.StartLine\", Ignore = true)]\n[ColumnMap(\"Location.EndLine\", Ignore = true)]", "Location")]
    public void Invalid_owned_map_reports_ZAO106(string attributes, string named)
    {
        var d = Assert.Single(Zao106(Owned + "[TableMap(typeof(Request), \"Requests\")]\n" + attributes + "\ninternal static class RequestMap;\n"));
        Assert.Contains("'" + named + "'", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
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

    // The assembly default is read once per compilation into a plain value: an edit that keeps it
    // leaves the map set cached, and changing it re-resolves the set without re-reading any map.
    [Fact]
    public void Assembly_default_change_re_resolves_the_set_but_not_the_map_reads()
    {
        var map = CSharpSyntaxTree.ParseText(MapSource, path: "/src/Map.cs");
        var defaults = CSharpSyntaxTree.ParseText("using ZeroAlloc.ORM;", path: "/src/Defaults.cs");
        var compilation = GeneratorHarness.CreateCompilation(new[] { map, defaults });

        var driver = CreateDriver().RunGenerators(compilation);
        var withComment = compilation.ReplaceSyntaxTree(
            defaults, defaults.WithChangedText(SourceText.From("using ZeroAlloc.ORM;\n// nothing changes\n")));
        driver = driver.RunGenerators(withComment);
        AssertCached(driver.GetRunResult().Results[0], "EntityMapDefaults", "EntityMapReads", "EntityMapSets");

        driver = driver.RunGenerators(withComment.ReplaceSyntaxTree(
            withComment.SyntaxTrees.Single(t => string.Equals(t.FilePath, "/src/Defaults.cs", StringComparison.Ordinal)),
            CSharpSyntaxTree.ParseText(
                "using ZeroAlloc.ORM;\n[assembly: EntityMapDefaults(DateTimeStorage = DateTimeStorage.TimestampTz)]",
                path: "/src/Defaults.cs")));
        var run = driver.GetRunResult().Results[0];
        AssertCached(run, "EntityMapReads");
        Assert.Contains(
            run.TrackedSteps["EntityMapDefaults"].SelectMany(s => s.Outputs),
            o => o.Reason == IncrementalStepRunReason.Modified);
    }
}
