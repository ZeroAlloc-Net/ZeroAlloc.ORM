using System.Data.Async.Adapters;
using AwesomeAssertions;
using Npgsql;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqCommand] ExecuteUpdate against a real PostgreSQL. Each case boots its own container, so
// the rows a case updates are its own.
[Trait("Provider", "Postgres")]
public sealed class PostgresLinqUpdateTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 30, 0, TimeSpan.Zero);

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

    private static async Task<Guid> InsertAsync(LinqUpdateRepo repo, AgentSessionState state, int turnCount = 0)
    {
        var id = Guid.NewGuid();
        await repo.InsertAsync(id, state.ToString(), Earlier, turnCount, CancellationToken.None).ConfigureAwait(false);
        return id;
    }

    // AgentSessionCrashRecovery: every running session becomes failed, with the recovery time as
    // its last activity and its end; the others keep their state and their time.
    [Fact]
    public async Task Crash_recovery_resets_running_sessions()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqUpdateRepo(fx.Connection);
        var first = await InsertAsync(repo, AgentSessionState.Running).ConfigureAwait(false);
        var second = await InsertAsync(repo, AgentSessionState.Running).ConfigureAwait(false);
        var idle = await InsertAsync(repo, AgentSessionState.Idle).ConfigureAwait(false);

        (await repo.FailRunningAsync(Now, CancellationToken.None).ConfigureAwait(false)).Should().Be(2);
        (await repo.FailRunningAsync(Now, CancellationToken.None).ConfigureAwait(false)).Should().Be(0);

        foreach (var id in new[] { first, second })
        {
            var session = await repo.GetAsync(id, CancellationToken.None).ConfigureAwait(false);
            session.Should().Be(new AgentSessionLine(id, AgentSessionState.Failed, Now, 0, 0, Now));
        }
        var untouched = await repo.GetAsync(idle, CancellationToken.None).ConfigureAwait(false);
        untouched.Should().Be(new AgentSessionLine(idle, AgentSessionState.Idle, Earlier, 0, 0, null));
    }

    // Two connections add turns to one session at the same time. Each UPDATE reads the counter
    // under its row lock, so no increment is lost: the total is exactly every turn of both.
    [Fact]
    public async Task Counters_increment_atomically()
    {
        const int TurnsPerConnection = 200;
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var id = await InsertAsync(new LinqUpdateRepo(fx.Connection), AgentSessionState.Running).ConfigureAwait(false);

        var raw = new NpgsqlConnection(fx.ConnectionString);
        await using (raw.ConfigureAwait(false))
        {
            var other = raw.AsAsync();
            await other.OpenAsync().ConfigureAwait(false);
            var repos = new[] { new LinqUpdateRepo(fx.Connection), new LinqUpdateRepo(other) };

            await Task.WhenAll(repos.Select(async repo =>
            {
                for (var i = 0; i < TurnsPerConnection; i++)
                    await repo.RecordTurnAsync(id, 3, CancellationToken.None).ConfigureAwait(false);
            })).ConfigureAwait(false);
        }

        var session = await new LinqUpdateRepo(fx.Connection).GetAsync(id, CancellationToken.None).ConfigureAwait(false);
        session!.TurnCount.Should().Be(2 * TurnsPerConnection);
        session.TotalInputTokens.Should().Be(2L * TurnsPerConnection * 3);
    }

    // Every SET item reads the row as it was before the statement, as PostgreSQL defines it: the
    // second setter adds the old TurnCount, 5, not the 6 the first one writes.
    [Fact]
    public async Task Computed_setters_read_the_row_before_the_update()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqUpdateRepo(fx.Connection);
        var id = await InsertAsync(repo, AgentSessionState.Running, 5).ConfigureAwait(false);

        (await repo.AdvanceAsync(id, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);

        var session = await repo.GetAsync(id, CancellationToken.None).ConfigureAwait(false);
        session!.TurnCount.Should().Be(6);
        session.TotalInputTokens.Should().Be(5);
    }

    // The state changes only from the state the caller read. Once it moved, the same swap
    // affects no row and the session keeps the state it moved to.
    [Fact]
    public async Task Compare_and_swap_returns_zero_when_state_moved()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqUpdateRepo(fx.Connection);
        var id = await InsertAsync(repo, AgentSessionState.Idle).ConfigureAwait(false);

        (await repo.TransitionAsync(id, AgentSessionState.Idle, AgentSessionState.Running, CancellationToken.None).ConfigureAwait(false))
            .Should().Be(1);
        (await repo.TransitionAsync(id, AgentSessionState.Idle, AgentSessionState.Failed, CancellationToken.None).ConfigureAwait(false))
            .Should().Be(0);

        var session = await repo.GetAsync(id, CancellationToken.None).ConfigureAwait(false);
        session!.State.Should().Be(AgentSessionState.Running);
    }

    // C# would wrap int.MaxValue + 1 to int.MinValue; PostgreSQL raises "integer out of range"
    // and the row keeps its value. A documented difference: the SQL never silently wraps.
    [Fact]
    public async Task Counter_overflow_raises_postgres_error()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqUpdateRepo(fx.Connection);
        var id = await InsertAsync(repo, AgentSessionState.Running, int.MaxValue).ConfigureAwait(false);

        var turn = () => repo.RecordTurnAsync(id, 1, CancellationToken.None);

        (await turn.Should().ThrowAsync<PostgresException>().ConfigureAwait(false))
            .Which.SqlState.Should().Be(PostgresErrorCodes.NumericValueOutOfRange);
        var session = await repo.GetAsync(id, CancellationToken.None).ConfigureAwait(false);
        session!.TurnCount.Should().Be(int.MaxValue);
        session.TotalInputTokens.Should().Be(0);
    }

    // Owned members are set through the map: the renamed "RepositoryUrl", and the optional
    // Outcome's default-named columns, whose [Json] ValidationResult binds as jsonb.
    [Fact]
    public async Task Owned_member_setter_writes_its_column()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var owned = new LinqOwnedTypeRepo(fx.Connection);
        var repo = new LinqUpdateRepo(fx.Connection);
        var id = Guid.NewGuid();
        await owned.InsertAsync(
            id, "a", "https://old", null, null, RepositoryPlatform.GitHub, "src/a.cs", null, null, null, null, null, CancellationToken.None)
            .ConfigureAwait(false);

        (await repo.RecordOutcomeAsync(id, "https://new", "https://new/pull/7", """{"ok": true}""", CancellationToken.None).ConfigureAwait(false))
            .Should().Be(1);

        var outcome = await repo.GetOutcomeAsync(id, CancellationToken.None).ConfigureAwait(false);
        outcome.Should().Be(new OutcomeLine("https://new", "https://new/pull/7", """{"ok": true}"""));

        (await repo.RecordOutcomeAsync(id, "https://new", null, null, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
        (await repo.GetOutcomeAsync(id, CancellationToken.None).ConfigureAwait(false))
            .Should().Be(new OutcomeLine("https://new", null, null));
    }
}
