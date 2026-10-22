using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] inner joins against a real PostgreSQL: each joined result equals LINQ to Objects'.
// Executions reference Tasks by TaskId, and Tasks reference app."Projects" by a ProjectId value
// object. Each case boots its own container.
[Trait("Provider", "Postgres")]
public sealed class PostgresLinqJoinTests
{
    private static readonly DateTime Day1 = new(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<PostgresFixture> CreateAsync()
    {
        PostgresFixture? fx = null;
        try
        {
            fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(LinqSchema.Ddl).ConfigureAwait(false);
            return fx;
        }
        catch
        {
            if (fx is not null)
            {
                await fx.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    // The seed shared by the Daedalus cases:
    //   Alpha: build, deploy.  Beta: review.  Gamma: no tasks.
    //   orphan: a task whose project does not exist.
    //   Executions of build, deploy, review and orphan, one before Day1, and one of no task.
    //   Alpha and Beta each have an execution with no model.
    private sealed record Seed(Guid Alpha, Guid Beta, Guid Build, Guid Deploy, Guid Review);

    private static async Task<Seed> SeedAsync(PostgresFixture fx)
    {
        var tasks = new LinqRepo(fx.Connection);
        var executions = new LinqAggregateRepo(fx.Connection);
        var ct = CancellationToken.None;
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var orphan = Guid.NewGuid();

        await tasks.InsertProjectAsync(seed.Alpha, "Alpha", 1, ct).ConfigureAwait(false);
        await tasks.InsertProjectAsync(seed.Beta, "Beta", 2, ct).ConfigureAwait(false);
        await tasks.InsertProjectAsync(Guid.NewGuid(), "Gamma", 3, ct).ConfigureAwait(false);
        await InsertTaskAsync(tasks, seed.Build, seed.Alpha, "build").ConfigureAwait(false);
        await InsertTaskAsync(tasks, seed.Deploy, seed.Alpha, "deploy").ConfigureAwait(false);
        await InsertTaskAsync(tasks, seed.Review, seed.Beta, "review").ConfigureAwait(false);
        await InsertTaskAsync(tasks, orphan, Guid.NewGuid(), "orphan").ConfigureAwait(false);

        await InsertExecutionAsync(executions, seed.Build, "sonnet", 10, 1, Day1.AddHours(1)).ConfigureAwait(false);
        await InsertExecutionAsync(executions, seed.Build, "sonnet", 20, 2, Day1.AddHours(2)).ConfigureAwait(false);
        await InsertExecutionAsync(executions, seed.Build, "opus", 5, 50, Day1.AddHours(3)).ConfigureAwait(false);
        await InsertExecutionAsync(executions, seed.Build, "sonnet", 500, 500, Day1.AddHours(-1)).ConfigureAwait(false);
        await InsertExecutionAsync(executions, seed.Deploy, "sonnet", 1, 1, Day1.AddHours(4)).ConfigureAwait(false);
        await InsertExecutionAsync(executions, seed.Review, null, 7, 7, Day1.AddHours(5)).ConfigureAwait(false);
        await InsertExecutionAsync(executions, seed.Deploy, null, 2, 3, Day1.AddHours(8)).ConfigureAwait(false);
        await InsertExecutionAsync(executions, orphan, "sonnet", 1000, 1000, Day1.AddHours(6)).ConfigureAwait(false);
        await InsertExecutionAsync(executions, Guid.NewGuid(), "sonnet", 999, 999, Day1.AddHours(7)).ConfigureAwait(false);
        return seed;
    }

    private static Task<int> InsertTaskAsync(LinqRepo repo, Guid id, Guid project, string title)
        => repo.InsertTaskAsync(
            id, new ProjectId(project), title, WorkStatus.Open, "Bug", new DateTimeOffset(Day1), null, [], 0, CancellationToken.None);

    private static Task<int> InsertExecutionAsync(
        LinqAggregateRepo repo,
        Guid task,
        string? model,
        int input,
        int output,
        DateTime executedAt)
        => repo.InsertAsync(
            Guid.NewGuid(), task, model, input, output, executedAt, null, null, null, null, CancellationToken.None);

    [Fact]
    public async Task Daedalus_q3_costs_by_project()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var seed = await SeedAsync(fx).ConfigureAwait(false);
        var repo = new LinqJoinRepo(fx.Connection);

        var rows = await repo.CostsByProjectAsync(Day1, CancellationToken.None).ConfigureAwait(false);

        // The execution before Day1 is filtered out; the orphan task's execution has no project
        // and the last execution no task, so the joins drop both. Gamma has no executions. The
        // null model sorts first within its project, as in LINQ.
        rows.Should().Equal(
            new ProjectModelCost(seed.Alpha, "Alpha", null, 2, 3, 1),
            new ProjectModelCost(seed.Alpha, "Alpha", "opus", 5, 50, 1),
            new ProjectModelCost(seed.Alpha, "Alpha", "sonnet", 31, 4, 3),
            new ProjectModelCost(seed.Beta, "Beta", null, 7, 7, 1));
    }

    [Fact]
    public async Task Daedalus_q4_costs_by_task_for_one_project()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var seed = await SeedAsync(fx).ConfigureAwait(false);
        var repo = new LinqJoinRepo(fx.Connection);
        var ct = CancellationToken.None;

        var alpha = await repo.CostsByTaskAsync(new ProjectId(seed.Alpha), ct).ConfigureAwait(false);
        var beta = await repo.CostsByTaskAsync(new ProjectId(seed.Beta), ct).ConfigureAwait(false);
        var none = await repo.CostsByTaskAsync(new ProjectId(Guid.NewGuid()), ct).ConfigureAwait(false);

        alpha.Should().Equal(
            new TaskCost(seed.Build, "build", 535, 4),
            new TaskCost(seed.Deploy, "deploy", 3, 2));
        beta.Should().Equal(new TaskCost(seed.Review, "review", 7, 1));
        none.Should().BeEmpty();
    }

    [Fact]
    public async Task Join_drops_unmatched_and_multiplies_duplicates()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var tasks = new LinqRepo(fx.Connection);
        var executions = new LinqAggregateRepo(fx.Connection);
        var repo = new LinqJoinRepo(fx.Connection);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var project = Guid.NewGuid();

        await InsertTaskAsync(tasks, a, project, "a").ConfigureAwait(false);
        await InsertTaskAsync(tasks, b, project, "b").ConfigureAwait(false);
        await InsertTaskAsync(tasks, Guid.NewGuid(), project, "idle").ConfigureAwait(false);
        await InsertExecutionAsync(executions, a, null, 2, 0, Day1).ConfigureAwait(false);
        await InsertExecutionAsync(executions, a, null, 1, 0, Day1).ConfigureAwait(false);
        await InsertExecutionAsync(executions, b, null, 3, 0, Day1).ConfigureAwait(false);
        await InsertExecutionAsync(executions, Guid.NewGuid(), null, 9, 0, Day1).ConfigureAwait(false);

        var rows = await repo.ExecutionTitlesAsync(CancellationToken.None).ConfigureAwait(false);

        // Task a pairs with each of its two executions; idle has none and the last execution no
        // task, so neither appears.
        rows.Should().Equal(
            new ExecutionTitle("a", 1),
            new ExecutionTitle("a", 2),
            new ExecutionTitle("b", 3));
    }

    [Fact]
    public async Task Same_column_name_in_both_tables_is_qualified()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var seed = await SeedAsync(fx).ConfigureAwait(false);
        var repo = new LinqJoinRepo(fx.Connection);

        var rows = await repo.TaskProjectIdsAsync(CancellationToken.None).ConfigureAwait(false);

        // Task.Id and Project.Id are both "Id"; each reads its own table's. Alpha's two tasks
        // repeat the project; the orphan task has no project.
        rows.Should().BeEquivalentTo(
            [
                new TaskProjectIds(seed.Build, seed.Alpha, "Alpha"),
                new TaskProjectIds(seed.Deploy, seed.Alpha, "Alpha"),
                new TaskProjectIds(seed.Review, seed.Beta, "Beta"),
            ],
            options => options.WithoutStrictOrdering());
        rows.Select(r => r.ProjectName).Should().Equal("Alpha", "Alpha", "Beta");
    }
}
