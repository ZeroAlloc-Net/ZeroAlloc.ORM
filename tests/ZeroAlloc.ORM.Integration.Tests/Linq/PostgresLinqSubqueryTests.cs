using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// A page of parents with the children of exactly that page, against a real PostgreSQL: the child
// statement's IN subquery repeats the parent statement's order and page, and both run as one
// batch. Each case boots its own container, so related checks share a case.
[Trait("Provider", "Postgres")]
public sealed class PostgresLinqSubqueryTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

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

    private static async Task<ExecutionOfTask> InsertExecutionAsync(LinqSubqueryRepo repo, Guid taskId, int inputTokens, int minutes)
    {
        var line = new ExecutionOfTask(Guid.NewGuid(), taskId, inputTokens);
        await repo.InsertExecutionAsync(line.Id, taskId, inputTokens, Start.AddMinutes(minutes), CancellationToken.None).ConfigureAwait(false);
        return line;
    }

    // TaskQueryService: the second page of tasks, newest first, and only their executions, in one
    // round trip.
    [Fact]
    public async Task Daedalus_paged_tasks_with_executions()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqSubqueryRepo(fx.Connection);
        var tasks = new List<TaskHead>();
        for (var i = 0; i < 5; i++)
        {
            var task = new TaskHead(Guid.NewGuid(), "task " + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await repo.InsertTaskAsync(task.Id, Guid.NewGuid(), task.Title, new DateTimeOffset(Start).AddHours(i), CancellationToken.None).ConfigureAwait(false);
            tasks.Add(task);
        }

        // Newest first: task 4, 3 | 2, 1 | 0. The second page is tasks 2 and 1.
        var executions = new List<ExecutionOfTask>();
        for (var i = 0; i < tasks.Count; i++)
        {
            executions.Add(await InsertExecutionAsync(repo, tasks[i].Id, 100 + i, minutes: 10 * i).ConfigureAwait(false));
            executions.Add(await InsertExecutionAsync(repo, tasks[i].Id, 200 + i, minutes: (10 * i) + 1).ConfigureAwait(false));
        }

        var (page, children) = await repo.PagedTasksWithExecutionsAsync(skip: 2, take: 2, CancellationToken.None).ConfigureAwait(false);

        page.Should().Equal(tasks[2], tasks[1]);
        children.Should().Equal(executions[2], executions[3], executions[4], executions[5]);

        // A page past the end has no parents and no children.
        var (none, noChildren) = await repo.PagedTasksWithExecutionsAsync(skip: 10, take: 2, CancellationToken.None).ConfigureAwait(false);
        none.Should().BeEmpty();
        noChildren.Should().BeEmpty();
    }

    // ProjectQueryService: projects in their Order, and the tasks of that page only, through the
    // task's typed ProjectId and a project table in its own schema.
    [Fact]
    public async Task Daedalus_paged_projects_with_tasks()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqSubqueryRepo(fx.Connection);
        var projects = new List<ProjectLine>();
        for (var i = 0; i < 3; i++)
        {
            var project = new ProjectLine(Guid.NewGuid(), i, "project " + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await repo.InsertProjectAsync(project.Id, project.Title, project.Order, CancellationToken.None).ConfigureAwait(false);
            projects.Add(project);
        }

        var at = new DateTimeOffset(Start);
        var first = new ProjectTask(Guid.NewGuid(), projects[0].Id, "a first");
        var second = new ProjectTask(Guid.NewGuid(), projects[1].Id, "b second");
        var third = new ProjectTask(Guid.NewGuid(), projects[1].Id, "c third");
        var last = new ProjectTask(Guid.NewGuid(), projects[2].Id, "d last");
        foreach (var task in new[] { last, third, second, first })
            await repo.InsertTaskAsync(task.Id, task.ProjectId, task.Title, at, CancellationToken.None).ConfigureAwait(false);

        var (page, tasks) = await repo.PagedProjectsWithTasksAsync(skip: 0, take: 2, CancellationToken.None).ConfigureAwait(false);
        page.Should().Equal(projects[0], projects[1]);
        tasks.Should().Equal(first, second, third);

        var (rest, restTasks) = await repo.PagedProjectsWithTasksAsync(skip: 2, take: 2, CancellationToken.None).ConfigureAwait(false);
        rest.Should().Equal(projects[2]);
        restTasks.Should().Equal(last);
    }

    // Parents whose order key ties: every task has the same CreatedAt, so only the Id tie-breaker
    // orders them. The page and the subquery order alike, so the children are exactly those of the
    // returned parents, page after page, and the pages cover every task once.
    [Fact]
    public async Task Paged_parents_and_children_match_with_tied_order_keys()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqSubqueryRepo(fx.Connection);
        var createdAt = new DateTimeOffset(Start);
        var executions = new List<ExecutionOfTask>();
        var ids = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            var id = Guid.NewGuid();
            await repo.InsertTaskAsync(id, Guid.NewGuid(), "tied", createdAt, CancellationToken.None).ConfigureAwait(false);
            executions.Add(await InsertExecutionAsync(repo, id, i, minutes: i).ConfigureAwait(false));
            ids.Add(id);
        }

        // PostgreSQL orders uuid by its bytes, which is the ordinal order of its text form.
        var expectedOrder = ids.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToList();
        var seen = new List<Guid>();
        seen.AddRange(await CheckPageAsync(0).ConfigureAwait(false));
        seen.AddRange(await CheckPageAsync(3).ConfigureAwait(false));
        seen.AddRange(await CheckPageAsync(6).ConfigureAwait(false));

        async Task<List<Guid>> CheckPageAsync(int skip)
        {
            var (page, children) = await repo.PagedTasksWithExecutionsAsync(skip, take: 3, CancellationToken.None).ConfigureAwait(false);

            var pageIds = page.Select(t => t.Id).ToList();
            pageIds.Should().Equal(expectedOrder.Skip(skip).Take(3));
            children.Should().Equal(executions.Where(e => pageIds.Contains(e.TaskId)));
            return pageIds;
        }

        seen.Should().Equal(expectedOrder);
    }
}
