using System.Globalization;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// A companion that does not compile cannot be read: its operation tree holds invalid operations,
// and the generator then emits no implementation, so CS8795 on the partial method hides the body's
// own errors. ZAO114 reports the compiler's error in their place, where it names the real cause.
// A companion that compiles but uses something unsupported keeps ZAO102.
public class LinqCompanionCompileErrorTests
{
    private const string Prelude = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;
        using ZeroAlloc.ORM.Linq;

        namespace TestApp
        {
            public sealed class Skill
            {
                private readonly List<string> _tags = [];
                public string Id { get; private set; } = "";
                public IReadOnlyList<string> Tags => _tags;
            }

            public sealed record SkillTagsRow(string Name, string[] Tags);

            public sealed record SkillNameRow(string Name);

            [TableMap(typeof(Skill), "Skills")]
            [ColumnMap(nameof(Skill.Id), Column = "Name")]
            [ColumnMap(nameof(Skill.Tags))]
            internal static class SkillMap;

        """;

    private const string Repo = """

            [Dialect(SqlDialect.PostgreSql)]
            internal sealed partial class SkillSql(IAsyncDbConnection connection)
            {
        """;

    private static (Diagnostic Diagnostic, string Text) RunSingle(string members, string extra = "")
    {
        var source = Prelude + extra + Repo + members + "\n    }\n}\n";
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(source);
        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Same(tree, diagnostic.Location.SourceTree);
        var span = diagnostic.Location.SourceSpan;
        return (diagnostic, tree.ToString().Substring(span.Start, span.Length));
    }

    // The projection passes IReadOnlyList<string> to a string[] parameter.
    [Fact]
    public void Type_mismatch_in_a_projection_reports_the_compiler_error()
    {
        var (diagnostic, located) = RunSingle("""
                [LinqQuery(nameof(SkillTagsQuery))]
                public partial Task<SkillTagsRow?> SkillTags(string name, CancellationToken ct);

                private static IQuery<SkillTagsRow> SkillTagsQuery(string name) =>
                    Sql.Table<Skill>().Where(s => s.Id == name).Select(s => new SkillTagsRow(s.Id, s.Tags));
            """);

        Assert.Equal("ZAO114", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("s.Tags", located);
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("'SkillTagsQuery'", message, System.StringComparison.Ordinal);
        Assert.Contains("[LinqQuery]", message, System.StringComparison.Ordinal);
        Assert.Contains("CS1503", message, System.StringComparison.Ordinal);
    }

    // A namespace named Sql beside the repository makes the bare Sql.Table bind to it.
    [Fact]
    public void Name_resolving_to_a_namespace_reports_the_compiler_error()
    {
        var (diagnostic, located) = RunSingle(
            """
                [LinqQuery(nameof(NamesQuery))]
                public partial Task<List<SkillNameRow>> Names(CancellationToken ct);

                private static IQuery<SkillNameRow> NamesQuery() =>
                    Sql.Table<Skill>().Select(s => new SkillNameRow(s.Id));
            """,
            extra: "    namespace Sql { internal static class Marker; }\n");

        Assert.Equal("ZAO114", diagnostic.Id);
        Assert.Equal("Sql.Table<Skill>", located);
        Assert.Contains("CS0234", diagnostic.GetMessage(CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }

    // A companion that compiles and uses something unsupported is still ZAO102.
    [Fact]
    public void Companion_that_compiles_keeps_ZAO102()
    {
        var (diagnostic, _) = RunSingle("""
                [LinqQuery(nameof(NamesQuery))]
                public partial Task<List<SkillNameRow>> Names(CancellationToken ct);

                private static IQuery<SkillNameRow> NamesQuery() =>
                    Sql.Table<Skill>().OrderBy(s => s.Id).GroupBy(s => s.Id).Select(g => new SkillNameRow(g.Key));
            """);

        Assert.Equal("ZAO102", diagnostic.Id);
    }
}
