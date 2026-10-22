using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// IQuery<T>.Contains inside a predicate: `<chain>.Select(p => p.Col).Contains(<outer value>)`
// renders `<operand> IN (SELECT "Col" FROM ... WHERE ... ORDER BY ... LIMIT ... OFFSET ...)`, so
// the children of a paged parent list load with the parent statement's own order and page.
public class LinqSubqueryTests
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

        namespace TestApp;

        public sealed class TaskEntity
        {
            public Guid Id { get; private set; }
            public Guid ProjectId { get; private set; }
            public Guid? ParentId { get; private set; }
            public string Title { get; private set; } = "";
            public DateTimeOffset CreatedAt { get; private set; }
            public Stage Stage { get; private set; }
            public long Weight { get; private set; }
        }

        public enum Stage { Todo, Done }

        public sealed class ExecutionEntity
        {
            public Guid Id { get; private set; }
            public Guid TaskId { get; private set; }
            public Guid? ParentTaskId { get; private set; }
            public int InputTokens { get; private set; }
            public DateTime ExecutedAt { get; private set; }
        }

        public sealed class ProjectEntity
        {
            public Guid Id { get; private set; }
            public string Name { get; private set; } = "";
        }

        [TableMap(typeof(TaskEntity), "Tasks")]
        [ColumnMap(nameof(TaskEntity.Stage), EnumAsString = true)]
        internal static class TaskMap;

        [TableMap(typeof(ExecutionEntity), "Executions")]
        internal static class ExecutionMap;

        [TableMap(typeof(ProjectEntity), "Projects")]
        internal static class ProjectMap;

        public sealed class OrphanEntity
        {
            public Guid Id { get; private set; }
        }

        public sealed record TaskHead(Guid Id, string Title);

        public sealed record ExecutionLine(Guid Id, Guid TaskId, int InputTokens);

        """;

    // The TaskQueryService shape: a page of tasks and the executions of exactly those tasks. The
    // subquery repeats the parent statement's ORDER BY, LIMIT and OFFSET, and both statements bind
    // skip and take.
    [Fact]
    public void Children_of_paged_parents()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class TaskStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(PageQuery))]
                public partial Task<(List<TaskHead> Tasks, List<ExecutionLine> Executions)> Page(int skip, int take, CancellationToken ct);

                private static (IQuery<TaskHead>, IQuery<ExecutionLine>) PageQuery(int skip, int take) =>
                    (Sql.Table<TaskEntity>()
                        .OrderByDescending(t => t.CreatedAt)
                        .ThenBy(t => t.Id)
                        .Skip(skip)
                        .Take(take)
                        .Select(t => new TaskHead(t.Id, t.Title)),
                     Sql.Table<ExecutionEntity>()
                        .Where(e => Sql.Table<TaskEntity>()
                            .OrderByDescending(t => t.CreatedAt)
                            .ThenBy(t => t.Id)
                            .Skip(skip)
                            .Take(take)
                            .Select(t => t.Id)
                            .Contains(e.TaskId))
                        .OrderBy(e => e.ExecutedAt)
                        .ThenBy(e => e.Id)
                        .Select(e => new ExecutionLine(e.Id, e.TaskId, e.InputTokens)));
            }
            """,
            "SELECT \"Id\", \"Title\" FROM \"Tasks\" ORDER BY \"CreatedAt\" DESC, \"Id\" LIMIT GREATEST(@take, 0) OFFSET GREATEST(@skip, 0)",
            "SELECT \"Id\", \"TaskId\", \"InputTokens\" FROM \"Executions\" WHERE \"TaskId\" IN (SELECT \"Id\" FROM \"Tasks\" ORDER BY \"CreatedAt\" DESC, \"Id\" LIMIT GREATEST(@take, 0) OFFSET GREATEST(@skip, 0)) ORDER BY \"ExecutedAt\", \"Id\"",
            "__cmd1.CommandText = \"SELECT \"Id\", \"TaskId\", \"InputTokens\" FROM \"Executions\" WHERE \"TaskId\" IN (",
            generated =>
            {
                // Both batch commands bind skip and take: the child reads them in its subquery.
                Assert.Contains("__p_take_0.ParameterName = \"take\"", generated, StringComparison.Ordinal);
                Assert.Contains("__p_skip_0.ParameterName = \"skip\"", generated, StringComparison.Ordinal);
                Assert.Contains("__p_take_1.ParameterName = \"take\"", generated, StringComparison.Ordinal);
                Assert.Contains("__p_skip_1.ParameterName = \"skip\"", generated, StringComparison.Ordinal);
            });
    }

    // C#'s `!` over a Contains renders NOT IN; the subquery's own Where binds its parameter.
    [Fact]
    public void Not_contains_renders_not_in()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class ExecutionStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(OutsideQuery))]
                public partial Task<List<ExecutionLine>> Outside(string title, CancellationToken ct);

                private static IQuery<ExecutionLine> OutsideQuery(string title) =>
                    Sql.Table<ExecutionEntity>()
                        .Where(e => !Sql.Table<TaskEntity>().Where(t => t.Title == title).Select(t => t.Id).Contains(e.TaskId))
                        .Select(e => new ExecutionLine(e.Id, e.TaskId, e.InputTokens));
            }
            """,
            "SELECT \"Id\", \"TaskId\", \"InputTokens\" FROM \"Executions\" WHERE \"TaskId\" NOT IN (SELECT \"Id\" FROM \"Tasks\" WHERE \"Title\" = @title)",
            "ParameterName = \"title\"",
            "// LINQ: ExecutionStore.OutsideQuery",
            static _ => { });
    }

    // Under an outer join aliased t0 and t1, a subquery with a join of its own is aliased s0 and
    // s1, and a subquery without one is not aliased at all.
    [Fact]
    public void Subquery_under_join_uses_its_own_aliases()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class ExecutionStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(InProjectQuery))]
                public partial Task<List<ExecutionLine>> InProject(string name, string title, CancellationToken ct);

                private static IQuery<ExecutionLine> InProjectQuery(string name, string title) =>
                    Sql.Table<ExecutionEntity>()
                        .Join(Sql.Table<TaskEntity>(), e => e.TaskId, t => t.Id, (e, t) => new { Execution = e, Task = t })
                        .Where(x => Sql.Table<TaskEntity>()
                            .Join(Sql.Table<ProjectEntity>(), t => t.ProjectId, p => p.Id, (t, p) => new { Task = t, Project = p })
                            .Where(y => y.Project.Name == name)
                            .Select(y => y.Task.Id)
                            .Contains(x.Task.Id))
                        .Where(x => Sql.Table<TaskEntity>().Where(t => t.Title == title).Select(t => t.Id).Contains(x.Execution.TaskId))
                        .Select(x => new ExecutionLine(x.Execution.Id, x.Execution.TaskId, x.Execution.InputTokens));
            }
            """,
            "FROM \"Executions\" AS t0 INNER JOIN \"Tasks\" AS t1 ON t0.\"TaskId\" = t1.\"Id\" WHERE ",
            "t1.\"Id\" IN (SELECT s0.\"Id\" FROM \"Tasks\" AS s0 INNER JOIN \"Projects\" AS s1 ON s0.\"ProjectId\" = s1.\"Id\" WHERE s1.\"Name\" = @name)",
            " AND t0.\"TaskId\" IN (SELECT \"Id\" FROM \"Tasks\" WHERE \"Title\" = @title)",
            "SELECT t0.\"Id\", t0.\"TaskId\", t0.\"InputTokens\" FROM",
            static _ => { });
    }

    // A filter on != null makes a nullable column eligible: the subquery then selects no NULL.
    [Fact]
    public void Not_null_filter_makes_a_nullable_column_eligible()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class ExecutionStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(UnderParentsQuery))]
                public partial Task<List<ExecutionLine>> UnderParents(CancellationToken ct);

                private static IQuery<ExecutionLine> UnderParentsQuery() =>
                    Sql.Table<ExecutionEntity>()
                        .Where(e => !Sql.Table<TaskEntity>().Where(t => t.ParentId != null).Select(t => t.ParentId).Contains(e.TaskId))
                        .Select(e => new ExecutionLine(e.Id, e.TaskId, e.InputTokens));
            }
            """,
            "WHERE \"TaskId\" NOT IN (SELECT \"ParentId\" FROM \"Tasks\" WHERE \"ParentId\" IS NOT NULL)",
            "// LINQ: ExecutionStore.UnderParentsQuery",
            "SELECT \"Id\", \"TaskId\", \"InputTokens\" FROM \"Executions\"",
            "__reader",
            static _ => { });
    }

    // The subquery keeps LINQ's null order on a nullable key, from its own null knowledge, and an
    // enum parameter it compares with a column stored by name binds by name.
    [Fact]
    public void Subquery_orders_nulls_like_linq_and_binds_enums_by_its_columns()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class ExecutionStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(FirstInStageQuery))]
                public partial Task<List<ExecutionLine>> FirstInStage(Stage stage, int take, CancellationToken ct);

                private static IQuery<ExecutionLine> FirstInStageQuery(Stage stage, int take) =>
                    Sql.Table<ExecutionEntity>()
                        .Where(e => Sql.Table<TaskEntity>()
                            .Where(t => t.Stage == stage)
                            .OrderBy(t => t.ParentId)
                            .ThenBy(t => t.Id)
                            .Take(take)
                            .Select(t => t.Id)
                            .Contains(e.TaskId))
                        .Select(e => new ExecutionLine(e.Id, e.TaskId, e.InputTokens));
            }
            """,
            "WHERE \"TaskId\" IN (SELECT \"Id\" FROM \"Tasks\" WHERE \"Stage\" = @stage ORDER BY \"ParentId\" NULLS FIRST, \"Id\" LIMIT GREATEST(@take, 0))",
            "__p_stage.Value = @stage.ToString();",
            "__p_take.ParameterName = \"take\"",
            static _ => { });
    }

    // A nullable outer value: IN is NULL for a NULL value, which the WHERE drops as C#'s false is,
    // and the negation takes IS NOT TRUE, which keeps it as C#'s true. The selected column is cast
    // to the nullable type, so Contains takes the value; it still selects no NULL.
    [Fact]
    public void Nullable_outer_value_reads_in_and_negates_with_is_not_true()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class ExecutionStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(UnderQuery))]
                public partial Task<List<ExecutionLine>> Under(string title, CancellationToken ct);

                [LinqQuery(nameof(NotUnderQuery))]
                public partial Task<List<ExecutionLine>> NotUnder(string title, CancellationToken ct);

                private static IQuery<ExecutionLine> UnderQuery(string title) =>
                    Sql.Table<ExecutionEntity>()
                        .Where(e => Sql.Table<TaskEntity>().Where(t => t.Title == title).Select(t => (Guid?)t.Id).Contains(e.ParentTaskId))
                        .Select(e => new ExecutionLine(e.Id, e.TaskId, e.InputTokens));

                private static IQuery<ExecutionLine> NotUnderQuery(string title) =>
                    Sql.Table<ExecutionEntity>()
                        .Where(e => !Sql.Table<TaskEntity>().Where(t => t.Title == title).Select(t => (Guid?)t.Id).Contains(e.ParentTaskId))
                        .Select(e => new ExecutionLine(e.Id, e.TaskId, e.InputTokens));
            }
            """,
            "FROM \"Executions\" WHERE \"ParentTaskId\" IN (SELECT \"Id\" FROM \"Tasks\" WHERE \"Title\" = @title)\"",
            "FROM \"Executions\" WHERE (\"ParentTaskId\" IN (SELECT \"Id\" FROM \"Tasks\" WHERE \"Title\" = @title)) IS NOT TRUE\"",
            "// LINQ: ExecutionStore.NotUnderQuery",
            static generated => Assert.DoesNotContain("NOT IN", generated, StringComparison.Ordinal));
    }

    // A subquery inside a subquery: the middle one joins, aliased s0 and s1, and the inner one in
    // its WHERE is plain and unaliased. Each binds through its own scope.
    [Fact]
    public void Nested_subquery_binds_through_its_own_scope()
    {
        VerifyCompiled(Prelude + """
            [Dialect(SqlDialect.PostgreSql)]
            public sealed partial class ExecutionStore(IAsyncDbConnection connection)
            {
                [LinqQuery(nameof(NestedQuery))]
                public partial Task<List<ExecutionLine>> Nested(string name, string title, CancellationToken ct);

                private static IQuery<ExecutionLine> NestedQuery(string name, string title) =>
                    Sql.Table<ExecutionEntity>()
                        .Where(e => Sql.Table<TaskEntity>()
                            .Join(Sql.Table<ProjectEntity>(), t => t.ProjectId, p => p.Id, (t, p) => new { Task = t, Project = p })
                            .Where(y => y.Project.Name == name
                                && Sql.Table<TaskEntity>().Where(t => t.Title == title).Select(t => t.Id).Contains(y.Task.Id))
                            .Select(y => y.Task.Id)
                            .Contains(e.TaskId))
                        .Select(e => new ExecutionLine(e.Id, e.TaskId, e.InputTokens));
            }
            """,
            "WHERE \"TaskId\" IN (SELECT s0.\"Id\" FROM \"Tasks\" AS s0 INNER JOIN \"Projects\" AS s1 ON s0.\"ProjectId\" = s1.\"Id\" WHERE s1.\"Name\" = @name AND s0.\"Id\" IN (SELECT \"Id\" FROM \"Tasks\" WHERE \"Title\" = @title))",
            "__p_name.ParameterName = \"name\"",
            "__p_title.ParameterName = \"title\"",
            static _ => { });
    }

    // The inner subquery reading the middle one's row is as correlated as reading the outer row.
    [Fact]
    public void Nested_subquery_reading_the_middle_row_reports_ZAO102()
    {
        var (located, message) = SingleError("""
            Sql.Table<TaskEntity>()
                .Where(y => Sql.Table<TaskEntity>().Where(t => t.ProjectId == y.ProjectId).Select(t => t.Id).Contains(y.Id))
                .Select(y => y.Id)
                .Contains(e.TaskId)
            """);

        Assert.Equal("y.ProjectId", located);
        Assert.Contains("correlated subquery", message, StringComparison.Ordinal);
    }

    // An unmapped entity in a subquery fails the query with one ZAO104, as at the top level, and
    // no ZAO105 for each of its members.
    [Fact]
    public void Unmapped_subquery_entity_reports_one_ZAO104()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Store(
            "Sql.Table<OrphanEntity>().OrderBy(o => o.Id).Select(o => o.Id).Contains(e.TaskId)"));

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO104", diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("Sql.Table<OrphanEntity>()", tree.ToString().Substring(span.Start, span.Length));
    }

    // A subquery that reads the outer row would be a correlated subquery, which is not translated.
    [Fact]
    public void Correlated_subquery_reports_ZAO102()
    {
        var (located, message) = SingleError(
            "Sql.Table<TaskEntity>().Where(t => t.Id == e.ParentTaskId).Select(t => t.Id).Contains(e.TaskId)");

        Assert.Equal("e.ParentTaskId", located);
        Assert.Equal(
            "'e.ParentTaskId', a read of the outer row inside a subquery, which makes it a correlated subquery, is not supported in a [LinqQuery] chain",
            message);
    }

    // SQL's NOT IN is NULL for every row once the subquery selects a NULL, unlike C#'s Contains, so
    // the selected column must be non-nullable, whether the outer value is nullable or not.
    [Theory]
    [InlineData(
        "Sql.Table<TaskEntity>().Select(t => t.ParentId).Contains(e.TaskId)",
        "t.ParentId",
        "Selecting 't.ParentId', which can be null, for an IQuery Contains without a 't.ParentId != null' filter in the subquery, is not supported in a [LinqQuery] chain")]
    [InlineData(
        "!Sql.Table<TaskEntity>().Where(t => t.Title == title).Select(t => t.ParentId).Contains(e.ParentTaskId)",
        "t.ParentId",
        "Selecting 't.ParentId', which can be null, for an IQuery Contains without a 't.ParentId != null' filter in the subquery, is not supported in a [LinqQuery] chain")]
    public void Nullable_subquery_column_reports_ZAO102(string predicate, string expectedLocation, string expectedMessage)
    {
        var (located, message) = SingleError(predicate);

        Assert.Equal(expectedLocation, located);
        Assert.Equal(expectedMessage, message);
    }

    // The subquery projects exactly one column, a mapped one, and ends with that Select. A cast
    // that converts the column, rather than only lifting it to its nullable type, is no column.
    [Theory]
    [InlineData("Sql.Table<TaskEntity>().Select(t => new TaskHead(t.Id, t.Title)).Contains(new TaskHead(e.TaskId, \"\"))", "new TaskHead(t.Id, t.Title)")]
    [InlineData("Sql.Table<TaskEntity>().Select(t => title).Contains(\"x\")", "title")]
    [InlineData("Sql.Table<TaskEntity>().Select(t => (int?)t.Weight).Contains(e.InputTokens)", "(int?)t.Weight")]
    [InlineData("Sql.Table<TaskEntity>().Where(t => t.Title == title).Contains(null!)", "Sql.Table<TaskEntity>().Where(t => t.Title == title)")]
    public void Subquery_must_select_one_column(string predicate, string expectedLocation)
    {
        var (located, message) = SingleError(predicate);

        Assert.Equal(expectedLocation, located);
        Assert.Contains("IQuery Contains", message, StringComparison.Ordinal);
    }

    // Skip or Take without OrderBy inside the subquery picks an arbitrary page of parents, as at
    // the top level. The query is still generated.
    [Fact]
    public void Unordered_paged_subquery_warns_ZAO110()
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Store("""
            Sql.Table<TaskEntity>().Where(t => t.Title == title).Take(10).Select(t => t.Id).Contains(e.TaskId)
            """));

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO110", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("Take", tree.ToString().Substring(span.Start, span.Length));
        Assert.Single(result.Results[0].GeneratedSources);
    }

    private static string Store(string predicate) => """
        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class ExecutionStore(IAsyncDbConnection connection)
        {
            [LinqQuery(nameof(FindQuery))]
            public partial Task<List<ExecutionLine>> Find(string title, CancellationToken ct);

            private static IQuery<ExecutionLine> FindQuery(string title) =>
                Sql.Table<ExecutionEntity>()
                    .Where(e =>
        """ + predicate + """
        )
                    .Select(e => new ExecutionLine(e.Id, e.TaskId, e.InputTokens));
        }
        """;

    private static (string Located, string Message) SingleError(string predicate)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(Prelude + Store(predicate));

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        var span = diagnostic.Location.SourceSpan;
        Assert.Same(tree, diagnostic.Location.SourceTree);
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.True(string.Equals(diagnostic.Id, "ZAO102", StringComparison.Ordinal), diagnostic.Id + ": " + message);
        return (tree.ToString().Substring(span.Start, span.Length), message);
    }

    // Each of `expected` is text the generated files must contain, checked before the snapshot.
    private static void VerifyCompiled(
        string source,
        string expected1,
        string expected2,
        string expected3,
        Action<string> check,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
        => VerifyCompiled(source, expected1, expected2, expected3, expected4: expected3, check, testFilePath, testMethod);

    private static void VerifyCompiled(
        string source,
        string expected1,
        string expected2,
        string expected3,
        string expected4,
        Action<string> check,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source);

        Assert.Empty(result.Results[0].Diagnostics);
        var generatedPaths = result.GeneratedTrees.Select(t => t.FilePath).ToHashSet(StringComparer.Ordinal);
        var problems = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                || (d.Severity == DiagnosticSeverity.Warning
                    && d.Location.SourceTree is { } tree
                    && generatedPaths.Contains(tree.FilePath)))
            .Select(d => d.Id + ": " + d.GetMessage(CultureInfo.InvariantCulture))
            .ToList();
        Assert.Empty(problems);

        // The SQL sits in C# string literals, which escape its quoted identifiers.
        var generated = string.Concat(result.GeneratedTrees.Select(t => t.ToString()))
            .Replace("\\\"", "\"", StringComparison.Ordinal);
        Assert.Contains(expected1, generated, StringComparison.Ordinal);
        Assert.Contains(expected2, generated, StringComparison.Ordinal);
        Assert.Contains(expected3, generated, StringComparison.Ordinal);
        Assert.Contains(expected4, generated, StringComparison.Ordinal);
        check(generated);

        GeneratorSnapshot.Verify(result, testFilePath, testMethod);
    }
}
