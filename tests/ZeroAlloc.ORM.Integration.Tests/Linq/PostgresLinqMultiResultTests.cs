using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// A parent with its children from one tuple companion, against a real PostgreSQL: each element
// is its own statement of one batch, and the method returns the tuple. Each case boots its own
// container, so related checks share a case.
[Trait("Provider", "Postgres")]
public sealed class PostgresLinqMultiResultTests
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

    private static async Task<ExecutionLine> InsertExecutionAsync(LinqMultiResultRepo repo, Guid taskId, int inputTokens, int minutes)
    {
        var line = new ExecutionLine(Guid.NewGuid(), inputTokens);
        await repo.InsertExecutionAsync(line.Id, taskId, inputTokens, Start.AddMinutes(minutes), CancellationToken.None).ConfigureAwait(false);
        return line;
    }

    // TaskRepository: one task by id with its executions, in ExecutedAt order, and none of
    // another task's. The scalar elements read the count and the average of the same rows.
    [Fact]
    public async Task Daedalus_task_with_executions_by_id()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqMultiResultRepo(fx.Connection);
        var task = new TaskHead(Guid.NewGuid(), "parse the logs");
        var other = Guid.NewGuid();
        await repo.InsertTaskAsync(task.Id, Guid.NewGuid(), task.Title, CancellationToken.None).ConfigureAwait(false);
        await repo.InsertTaskAsync(other, Guid.NewGuid(), "other", CancellationToken.None).ConfigureAwait(false);
        var second = await InsertExecutionAsync(repo, task.Id, 300, minutes: 20).ConfigureAwait(false);
        var first = await InsertExecutionAsync(repo, task.Id, 100, minutes: 10).ConfigureAwait(false);
        await InsertExecutionAsync(repo, other, 999, minutes: 5).ConfigureAwait(false);

        var (head, executions) = await repo.TaskWithExecutionsAsync(task.Id, CancellationToken.None).ConfigureAwait(false);
        head.Should().Be(task);
        executions.Should().Equal(first, second);

        var stats = await repo.TaskWithStatsAsync(task.Id, CancellationToken.None).ConfigureAwait(false);
        stats.Head.Should().Be(task);
        stats.Executions.Should().Equal(first, second);
        stats.Count.Should().Be(2);
        stats.AverageInput.Should().Be(200d);
    }

    // BrainstormRepository: one session with its messages, oldest first, written out of order.
    [Fact]
    public async Task Daedalus_session_with_ordered_messages()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqMultiResultRepo(fx.Connection);
        var session = new SessionHead(Guid.NewGuid(), "naming");
        var other = Guid.NewGuid();
        await repo.InsertSessionAsync(session.Id, session.Topic, CancellationToken.None).ConfigureAwait(false);
        await repo.InsertSessionAsync(other, "other", CancellationToken.None).ConfigureAwait(false);
        var at = new DateTimeOffset(Start);
        await repo.InsertMessageAsync(Guid.NewGuid(), session.Id, "assistant", "how about Daedalus", at.AddMinutes(2), CancellationToken.None).ConfigureAwait(false);
        await repo.InsertMessageAsync(Guid.NewGuid(), session.Id, "user", "pick a name", at, CancellationToken.None).ConfigureAwait(false);
        await repo.InsertMessageAsync(Guid.NewGuid(), session.Id, "user", "yes", at.AddMinutes(5), CancellationToken.None).ConfigureAwait(false);
        await repo.InsertMessageAsync(Guid.NewGuid(), other, "user", "elsewhere", at.AddMinutes(1), CancellationToken.None).ConfigureAwait(false);

        var (head, messages) = await repo.SessionWithMessagesAsync(session.Id, CancellationToken.None).ConfigureAwait(false);

        head.Should().Be(session);
        messages.Should().Equal(
            new MessageLine("user", "pick a name"),
            new MessageLine("assistant", "how about Daedalus"),
            new MessageLine("user", "yes"));
    }

    // A non-nullable tuple reads a missing parent as a null head and no children. A Count is 0
    // over no rows, while a non-nullable Average throws, as LINQ's Average does.
    [Fact]
    public async Task Missing_parent_returns_null_head_and_no_children()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqMultiResultRepo(fx.Connection);
        var task = Guid.NewGuid();
        await repo.InsertTaskAsync(task, Guid.NewGuid(), "present", CancellationToken.None).ConfigureAwait(false);
        await InsertExecutionAsync(repo, task, 100, minutes: 1).ConfigureAwait(false);

        var (head, executions) = await repo.TaskWithExecutionsAsync(Guid.NewGuid(), CancellationToken.None).ConfigureAwait(false);
        head.Should().BeNull();
        executions.Should().BeEmpty();

        // A task without executions: its head, and no children.
        var lonely = Guid.NewGuid();
        await repo.InsertTaskAsync(lonely, Guid.NewGuid(), "lonely", CancellationToken.None).ConfigureAwait(false);
        var withoutChildren = await repo.TaskWithExecutionsAsync(lonely, CancellationToken.None).ConfigureAwait(false);
        withoutChildren.Head.Should().Be(new TaskHead(lonely, "lonely"));
        withoutChildren.Executions.Should().BeEmpty();

        var stats = async () => await repo.TaskWithStatsAsync(lonely, CancellationToken.None).ConfigureAwait(false);
        (await stats.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false))
            .WithMessage("Sequence contains no elements");
    }

    // A parameter only the child statement reads binds on that statement's command, and the
    // statements that do not read it run without it.
    [Fact]
    public async Task Parameter_used_only_by_child_statement()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqMultiResultRepo(fx.Connection);
        var task = new TaskHead(Guid.NewGuid(), "filter the children");
        await repo.InsertTaskAsync(task.Id, Guid.NewGuid(), task.Title, CancellationToken.None).ConfigureAwait(false);
        await InsertExecutionAsync(repo, task.Id, 50, minutes: 1).ConfigureAwait(false);
        var large = await InsertExecutionAsync(repo, task.Id, 500, minutes: 2).ConfigureAwait(false);
        var larger = await InsertExecutionAsync(repo, task.Id, 900, minutes: 3).ConfigureAwait(false);

        var (head, executions) = await repo.TaskWithLargeExecutionsAsync(task.Id, 500, CancellationToken.None).ConfigureAwait(false);
        head.Should().Be(task);
        executions.Should().Equal(large, larger);

        var (missing, none) = await repo.TaskWithLargeExecutionsAsync(Guid.NewGuid(), 0, CancellationToken.None).ConfigureAwait(false);
        missing.Should().BeNull();
        none.Should().BeEmpty();
    }

    // A nullable outer tuple keeps [Query]'s meaning: null when the parent's result set is empty.
    [Fact]
    public async Task Nullable_outer_tuple_is_null_without_parent()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqMultiResultRepo(fx.Connection);
        var task = new TaskHead(Guid.NewGuid(), "present");
        await repo.InsertTaskAsync(task.Id, Guid.NewGuid(), task.Title, CancellationToken.None).ConfigureAwait(false);
        var execution = await InsertExecutionAsync(repo, task.Id, 10, minutes: 1).ConfigureAwait(false);

        (await repo.TaskWithExecutionsOrNullAsync(Guid.NewGuid(), CancellationToken.None).ConfigureAwait(false))
            .Should().BeNull();

        var found = await repo.TaskWithExecutionsOrNullAsync(task.Id, CancellationToken.None).ConfigureAwait(false);
        found.Should().NotBeNull();
        found!.Value.Head.Should().Be(task);
        found.Value.Executions.Should().Equal(execution);
    }
}
