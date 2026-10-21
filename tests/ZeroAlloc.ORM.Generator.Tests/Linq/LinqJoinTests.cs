using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.ORM.Generator.Linq;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Linq;

// [LinqQuery] Join: an inner join renders as INNER JOIN, every source aliased t0, t1, ... in
// join order, and every column qualified by the alias of the source it belongs to. Members of
// the join's anonymous result, such as x.Task.Title, resolve to their source's alias and that
// source's map, so a schema and a renamed column work. A query without a join is not aliased.
// Every snapshot is also compiled: no error anywhere and no warning in the emitted code.
public class LinqJoinTests
{
    // Projects live in schema app with Title in column "Name"; Executions reference Tasks; a
    // Budget has a ProjectId value object, as a Task does.
    private const string Entities = """

        public sealed class ProjectEntity
        {
            public Guid Id { get; private set; }
            public string Title { get; private set; } = "";
            public int Order { get; private set; }
        }

        [TableMap(typeof(ProjectEntity), "Projects", Schema = "app")]
        [ColumnMap(nameof(ProjectEntity.Title), Column = "Name")]
        internal static class ProjectMap;

        public sealed class ExecutionEntity
        {
            public Guid Id { get; private set; }
            public Guid TaskId { get; private set; }
            public string? ModelId { get; private set; }
            public int InputTokens { get; private set; }
            public int OutputTokens { get; private set; }
            public DateTime ExecutedAt { get; private set; }
        }

        [TableMap(typeof(ExecutionEntity), "Executions")]
        internal static class ExecutionMap;

        public sealed class BudgetEntity
        {
            public Guid Id { get; private set; }
            public ProjectId ProjectId { get; private set; }
            public decimal Amount { get; private set; }
            public decimal Doubled => Amount * 2;
            public ProjectId? ParentProjectId { get; private set; }
            public int Value { get; private set; }
        }

        [TableMap(typeof(BudgetEntity), "Budgets")]
        internal static class BudgetMap;

        public sealed record ExecutionTask(Guid ExecutionId, Guid TaskId, string Title, WorkKind Kind, int? Priority);

        public sealed record ProjectModelCost(Guid ProjectId, string ProjectName, string? ModelId, long InputTokens, long OutputTokens, int Executions);

        public sealed record TaskCost(Guid TaskId, string Title, long InputTokens, int Executions);

        public sealed record TaskProject(Guid TaskId, string ProjectName);

        public sealed record TaskBudget(Guid TaskId, decimal Amount);

        public sealed record TaskSibling(Guid TaskId, Guid SiblingId, WorkKind SiblingKind);

        public sealed record BudgetValue(Guid Id, int Value);

        """;

    private const string Repo = """
        [Dialect(SqlDialect.PostgreSql)]
        public sealed partial class TaskStore(IAsyncDbConnection connection)
        {

        """;

    // One join, both sides projected. The later Where, OrderBy and Select read x.Execution and
    // x.Task: null compensation follows each column's own type, an enum parameter compared with
    // the Task's EnumAsString Kind binds by name, the Kind column reads back by name, and the
    // nullable Priority orders NULLS FIRST. A count and a paged sum read the same join.
    [Fact]
    public void Single_join_projects_both_sides()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(ExecutionTasksQuery))]
                public partial Task<List<ExecutionTask>> ExecutionTasks(int minTokens, string? note, WorkKind kind, CancellationToken ct);

                [LinqQuery(nameof(CountQuery))]
                public partial Task<int> CountExecutionsOfActiveTasks(CancellationToken ct);

                [LinqQuery(nameof(TopInputQuery))]
                public partial Task<long> TopInput(int take, CancellationToken ct);

                private static IQuery<ExecutionTask> ExecutionTasksQuery(int minTokens, string? note, WorkKind kind) =>
                    Sql.Table<ExecutionEntity>()
                        .Join(Sql.Table<TaskEntity>(), e => e.TaskId, t => t.Id, (e, t) => new { Execution = e, Task = t })
                        .Where(x => x.Execution.InputTokens >= minTokens && x.Task.Note != note && x.Task.Kind == kind)
                        .OrderBy(x => x.Task.Priority)
                        .ThenBy(x => x.Execution.Id)
                        .Select(x => new ExecutionTask(x.Execution.Id, x.Task.Id, x.Task.Title, x.Task.Kind, x.Task.Priority));

                private static IQuery<int> CountQuery() =>
                    Sql.Table<ExecutionEntity>()
                        .Join(Sql.Table<TaskEntity>(), e => e.TaskId, t => t.Id, (e, t) => new { e, t })
                        .Where(x => x.t.IsActive)
                        .Count();

                private static IQuery<long> TopInputQuery(int take) =>
                    Sql.Table<ExecutionEntity>()
                        .Join(Sql.Table<TaskEntity>(), e => e.TaskId, t => t.Id, (e, t) => new { e, t })
                        .OrderByDescending(x => x.t.Order)
                        .Take(take)
                        .Sum(x => (long)x.e.InputTokens);
            }
            """ + Entities);
    }

    // Daedalus' Q3: Executions to Tasks to Projects, grouped by the project's id and name and
    // the model. The second join keys on a member carried over from the first join's result,
    // and its result carries the first two sources over by name.
    [Fact]
    public void Two_chained_joins_with_group_by()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(CostsByProjectQuery))]
                public partial Task<List<ProjectModelCost>> CostsByProject(DateTime since, CancellationToken ct);

                private static IQuery<ProjectModelCost> CostsByProjectQuery(DateTime since) =>
                    Sql.Table<ExecutionEntity>()
                        .Where(e => e.ExecutedAt >= since)
                        .Join(Sql.Table<TaskEntity>(), e => e.TaskId, t => t.Id, (e, t) => new { Execution = e, Task = t })
                        .Join(Sql.Table<ProjectEntity>(), et => et.Task.ProjectId.Value, p => p.Id, (et, p) => new { et.Execution, et.Task, Project = p })
                        .GroupBy(x => new { x.Project.Id, x.Project.Title, x.Execution.ModelId })
                        .OrderBy(g => g.Key.Title)
                        .ThenByDescending(g => g.Sum(x => (long)x.Execution.InputTokens))
                        .Select(g => new ProjectModelCost(
                            g.Key.Id,
                            g.Key.Title,
                            g.Key.ModelId,
                            g.Sum(x => (long)x.Execution.InputTokens),
                            g.Sum(x => (long)x.Execution.OutputTokens),
                            g.Count()));
            }
            """ + Entities);
    }

    // Daedalus' Q4: the inner source is filtered by a companion parameter. Its predicate goes
    // into the ON clause, scoped to the inner alias, and the parameter is bound.
    [Fact]
    public void Filtered_inner_source()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(CostsByTaskQuery))]
                public partial Task<List<TaskCost>> CostsByTask(ProjectId projectId, CancellationToken ct);

                private static IQuery<TaskCost> CostsByTaskQuery(ProjectId projectId) =>
                    Sql.Table<ExecutionEntity>()
                        .Join(
                            Sql.Table<TaskEntity>().Where(t => t.ProjectId == projectId && t.IsActive),
                            e => e.TaskId,
                            t => t.Id,
                            (e, t) => new { Execution = e, Task = t })
                        .GroupBy(x => new { x.Task.Id, x.Task.Title })
                        .OrderBy(g => g.Key.Title)
                        .Select(g => new TaskCost(g.Key.Id, g.Key.Title, g.Sum(x => (long)x.Execution.InputTokens), g.Count()));
            }
            """ + Entities);
    }

    // A value-object key joins on its column: `t.ProjectId.Value` against a Guid id, and a
    // ProjectId against a ProjectId. The second result is the inner range variable alone.
    [Fact]
    public void Value_object_key_against_underlying_column()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(TaskProjectsQuery))]
                public partial Task<List<TaskProject>> TaskProjects(CancellationToken ct);

                [LinqQuery(nameof(TaskBudgetsQuery))]
                public partial Task<List<TaskBudget>> TaskBudgets(CancellationToken ct);

                private static IQuery<TaskProject> TaskProjectsQuery() =>
                    Sql.Table<TaskEntity>()
                        .Join(Sql.Table<ProjectEntity>(), t => t.ProjectId.Value, p => p.Id, (t, p) => new { Task = t, Project = p })
                        .Select(x => new TaskProject(x.Task.Id, x.Project.Title));

                private static IQuery<TaskBudget> TaskBudgetsQuery() =>
                    Sql.Table<TaskEntity>()
                        .Join(Sql.Table<BudgetEntity>(), t => t.ProjectId, b => b.ProjectId, (t, b) => new { Task = t, Budget = b })
                        .Select(x => new TaskBudget(x.Task.Id, x.Budget.Amount));
            }
            """ + Entities);
    }

    // A composite key, a computed or column member in the result, any operator but Where on the
    // inner side, and a Join after GroupBy, Select, OrderBy or paging are not translated.
    [Theory]
    [InlineData(".Join(Sql.Table<BudgetEntity>(), t => new { t.ProjectId, t.Id }, b => new { b.ProjectId, b.Id }, (t, b) => new { Task = t, Budget = b }).Select(x => new TaskBudget(x.Task.Id, x.Budget.Amount))", "new { t.ProjectId, t.Id }")]
    [InlineData(".Join(Sql.Table<BudgetEntity>(), t => t.ProjectId, b => b.ProjectId, (t, b) => new { Task = t, Twice = b.Amount * 2 }).Select(x => new TaskBudget(x.Task.Id, x.Twice))", "Twice = b.Amount * 2")]
    [InlineData(".Join(Sql.Table<BudgetEntity>(), t => t.ProjectId, b => b.ProjectId, (t, b) => new { Task = t, b.Amount }).Select(x => new TaskBudget(x.Task.Id, x.Amount))", "b.Amount")]
    [InlineData(".Join(Sql.Table<BudgetEntity>().Select(b => b), t => t.ProjectId, b => b.ProjectId, (t, b) => new { Task = t, Budget = b }).Select(x => new TaskBudget(x.Task.Id, x.Budget.Amount))", "Select")]
    [InlineData(".Join(Sql.Table<BudgetEntity>().OrderBy(b => b.Amount), t => t.ProjectId, b => b.ProjectId, (t, b) => new { Task = t, Budget = b }).Select(x => new TaskBudget(x.Task.Id, x.Budget.Amount))", "OrderBy")]
    [InlineData(".Join(Sql.Table<BudgetEntity>(), t => t.Order + 1, b => 1, (t, b) => new { Task = t, Budget = b }).Select(x => new TaskBudget(x.Task.Id, x.Budget.Amount))", "t.Order + 1")]
    [InlineData(".OrderBy(t => t.Order).Join(Sql.Table<BudgetEntity>(), t => t.ProjectId, b => b.ProjectId, (t, b) => new { Task = t, Budget = b }).Select(x => new TaskBudget(x.Task.Id, x.Budget.Amount))", "Join")]
    [InlineData(".Take(3).Join(Sql.Table<BudgetEntity>(), t => t.ProjectId, b => b.ProjectId, (t, b) => new { Task = t, Budget = b }).Select(x => new TaskBudget(x.Task.Id, x.Budget.Amount))", "Join")]
    [InlineData(".GroupBy(t => t.ProjectId).Join(Sql.Table<BudgetEntity>(), g => g.Key, b => b.ProjectId, (g, b) => new { Group = g, Budget = b }).Select(x => new TaskBudget(Guid.Empty, x.Budget.Amount))", "Join")]
    [InlineData(".Select(t => new TaskBudget(t.Id, t.Price)).Join(Sql.Table<BudgetEntity>(), r => r.TaskId, b => b.Id, (r, b) => new { Row = r, Budget = b }).Select(x => new TaskBudget(x.Row.TaskId, x.Budget.Amount))", "Join")]
    [InlineData(".Join(Sql.Table<BudgetEntity>(), t => t.ProjectId.Value, b => b.ParentProjectId!.Value.Value, (t, b) => new { Task = t, Budget = b }).Select(x => new TaskBudget(x.Task.Id, x.Budget.Amount))", "b.ParentProjectId!.Value.Value")]
    [InlineData(".Join(Sql.Table<BudgetEntity>(), t => t.ProjectId, b => b.ParentProjectId!.Value, (t, b) => new { Task = t, Budget = b }).Select(x => new TaskBudget(x.Task.Id, x.Budget.Amount))", "b.ParentProjectId!.Value")]
    [InlineData(".Join(Sql.Table<BudgetEntity>(), t => t.ProjectId, b => b.ProjectId, (t, b) => new { Task = t, Budget = b }).Where(x => x.Budget.ParentProjectId!.Value == x.Task.ProjectId).Select(x => new TaskBudget(x.Task.Id, x.Budget.Amount))", "x.Budget.ParentProjectId!.Value")]
    public void Unsupported_join_forms_report_ZAO102(string chain, string located)
    {
        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(LinqWhereTests.Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskBudget>> Find(CancellationToken ct);

                private static IQuery<TaskBudget> FindQuery() =>
                    Sql.Table<TaskEntity>()
            """ + chain + """
            ;
            }
            """ + Entities);

        var diagnostic = Assert.Single(result.Results[0].Diagnostics);
        Assert.Equal("ZAO102", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal(located, tree.ToString().Substring(span.Start, span.Length));
    }

    // A self-join: the same table twice, each under its own alias and through the same map, so
    // its EnumAsString Kind reads by name on the joined side too.
    [Fact]
    public void Self_join_aliases_each_side()
    {
        VerifyCompiled(Repo + """
                [LinqQuery(nameof(SiblingsQuery))]
                public partial Task<List<TaskSibling>> Siblings(CancellationToken ct);

                private static IQuery<TaskSibling> SiblingsQuery() =>
                    Sql.Table<TaskEntity>()
                        .Join(Sql.Table<TaskEntity>(), a => a.ProjectId, b => b.ProjectId, (a, b) => new { Task = a, Sibling = b })
                        .Where(x => x.Task.Id != x.Sibling.Id)
                        .OrderBy(x => x.Sibling.Order)
                        .Select(x => new TaskSibling(x.Task.Id, x.Sibling.Id, x.Sibling.Kind));
            }
            """ + Entities);
    }

    // `.Value` is peeled only off a value object's own Value property: an entity column that is
    // itself named Value is that column.
    [Fact]
    public void Column_named_Value_reads_as_the_column()
    {
        var method = BindOnly(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<BudgetValue>> Find(CancellationToken ct);

                private static IQuery<BudgetValue> FindQuery() =>
                    Sql.Table<BudgetEntity>().Where(b => b.Value > 1).Select(b => new BudgetValue(b.Id, b.Value));
            }
            """ + Entities);
        Assert.Equal("SELECT \"Id\", \"Value\" FROM \"Budgets\" WHERE \"Value\" > 1", method.Sql);
    }

    // A joined source needs a map, as the FROM source does; a member it does not map names
    // its own entity.
    [Theory]
    [InlineData("Sql.Table<Unmapped>()", "(e, u) => new { Execution = e, Other = u }", "x.Execution.InputTokens", "ZAO104", "Sql.Table<Unmapped>()")]
    [InlineData("Sql.Table<BudgetEntity>()", "(e, b) => new { Execution = e, Other = b }", "x.Other.Doubled", "ZAO105", "x.Other.Doubled")]
    public void Joined_source_resolves_through_its_own_map(string inner, string result, string projected, string id, string located)
    {
        var (run, tree) = GeneratorHarness.RunGeneratorOnFile(LinqWhereTests.Prelude + Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<Picked>> Find(CancellationToken ct);

                private static IQuery<Picked> FindQuery() =>
                    Sql.Table<ExecutionEntity>()
                        .Join(
            """ + inner + ", e => e.TaskId, u => u.Id, " + result + ")" + """

                        .Select(x => new Picked(
            """ + projected + """
            ));
            }

            public sealed class Unmapped
            {
                public Guid Id { get; private set; }
            }

            public sealed record Picked(decimal Value);
            """ + Entities);

        var diagnostic = Assert.Single(run.Results[0].Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(id, diagnostic.Id);
        var span = diagnostic.Location.SourceSpan;
        Assert.Equal(located, tree.ToString().Substring(span.Start, span.Length));
        if (string.Equals(id, "ZAO105", StringComparison.Ordinal))
            Assert.Contains("TestApp.BudgetEntity", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // Plan 1's output is pinned: without a join, no alias and no qualifier anywhere, including
    // a paged scalar aggregate, which selects its column in a subquery.
    [Fact]
    public void Query_without_join_renders_unaliased()
    {
        var list = BindOnly(Repo + """
                [LinqQuery(nameof(FindQuery))]
                public partial Task<List<TaskSummary>> Find(int order, string? note, CancellationToken ct);

                private static IQuery<TaskSummary> FindQuery(int order, string? note) =>
                    Sql.Table<TaskEntity>()
                        .Where(t => t.Order > order && t.Note != note)
                        .OrderBy(t => t.Priority)
                        .Select(t => new TaskSummary(t.Id, t.Title));
            }
            """);
        Assert.Equal(
            "SELECT \"Id\", \"Title\" FROM \"Tasks\" WHERE \"Order\" > @order"
                + " AND ((\"Note\" <> @note OR (\"Note\" IS NULL AND @note IS NOT NULL)) OR (\"Note\" IS NOT NULL AND @note IS NULL))"
                + " ORDER BY \"Priority\" NULLS FIRST",
            list.Sql);

        var sum = BindOnly(Repo + """
                [LinqQuery(nameof(SumQuery))]
                public partial Task<long> Sum(int take, CancellationToken ct);

                private static IQuery<long> SumQuery(int take) =>
                    Sql.Table<TaskEntity>().OrderBy(t => t.Order).Take(take).Sum(t => t.Size);
            }
            """);
        Assert.Equal(
            "SELECT COALESCE(sum(\"Size\"), 0)::bigint FROM (SELECT \"Size\" FROM \"Tasks\" ORDER BY \"Order\" LIMIT GREATEST(@take, 0)) AS q",
            sum.Sql);
    }

    private static ZeroAlloc.ORM.Generator.Model.QueryMethodModel BindOnly(string body)
    {
        var compilation = GeneratorHarness.CreateCompilation([CSharpSyntaxTree.ParseText(LinqWhereTests.Prelude + body, path: "/src/Store.cs")]);
        var driver = CSharpGeneratorDriver.Create(
            new[] { new OrmGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        var run = driver.RunGenerators(compilation).GetRunResult().Results[0];
        Assert.Empty(run.Diagnostics);
        var output = Assert.Single(Assert.Single(run.TrackedSteps["LinqQueryMethods"]).Outputs);
        var result = Assert.IsType<LinqMethodResult>(output.Value);
        return result.Method!.Method;
    }

    private static void VerifyCompiled(
        string body,
        [CallerFilePath] string testFilePath = "",
        [CallerMemberName] string testMethod = "")
    {
        var (result, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(LinqWhereTests.Prelude + body);

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

        GeneratorSnapshot.Verify(result, testFilePath, testMethod);
    }
}
