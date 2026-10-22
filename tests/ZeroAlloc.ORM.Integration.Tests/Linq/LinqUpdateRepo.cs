using System.Data.Async;
using ZeroAlloc.ORM.Linq;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqCommand] updates the way Daedalus' stores use EF's ExecuteUpdate: crash recovery, a
// turn's counters, a compare-and-swap state change, and an owned object's members. Plus a
// [Command] insert to seed sessions and [LinqQuery] reads to check them.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class LinqUpdateRepo(IAsyncDbConnection connection)
{
    [Command("""
        INSERT INTO "AgentSessions" ("Id", "State", "LastActivityAt", "TurnCount", "TotalInputTokens")
        VALUES (@id, @state, @lastActivityAt, @turnCount, 0)
        """)]
    public partial Task<int> InsertAsync(Guid id, string state, DateTimeOffset lastActivityAt, int turnCount, CancellationToken ct);

    [LinqCommand(nameof(FailRunningQuery))]
    public partial Task<int> FailRunningAsync(DateTimeOffset now, CancellationToken ct);

    [LinqCommand(nameof(RecordTurnQuery))]
    public partial Task RecordTurnAsync(Guid id, int inputTokens, CancellationToken ct);

    [LinqCommand(nameof(AdvanceQuery))]
    public partial Task<int> AdvanceAsync(Guid id, CancellationToken ct);

    [LinqCommand(nameof(TransitionQuery))]
    public partial ValueTask<int> TransitionAsync(Guid id, AgentSessionState from, AgentSessionState to, CancellationToken ct);

    [LinqCommand(nameof(RecordOutcomeQuery))]
    public partial Task<int> RecordOutcomeAsync(Guid id, string url, string? pullRequestUrl, string? validation, CancellationToken ct);

    [LinqQuery(nameof(SessionQuery))]
    public partial Task<AgentSessionLine?> GetAsync(Guid id, CancellationToken ct);

    [LinqQuery(nameof(OutcomeQuery))]
    public partial Task<OutcomeLine?> GetOutcomeAsync(Guid id, CancellationToken ct);

    private static int FailRunningQuery(DateTimeOffset now) =>
        Sql.Table<AgentSessionEntity>()
            .Where(s => s.State == AgentSessionState.Running)
            .ExecuteUpdate(set => set
                .SetProperty(s => s.State, AgentSessionState.Failed)
                .SetProperty(s => s.LastActivityAt, now)
                .SetProperty(s => s.EndedAt, now));

    private static int RecordTurnQuery(Guid id, int inputTokens) =>
        Sql.Table<AgentSessionEntity>()
            .Where(s => s.Id == id)
            .ExecuteUpdate(set => set
                .SetProperty(s => s.TurnCount, s => s.TurnCount + 1)
                .SetProperty(s => s.TotalInputTokens, s => s.TotalInputTokens + inputTokens));

    // TotalInputTokens adds TurnCount as it was before this statement, not after its + 1.
    private static int AdvanceQuery(Guid id) =>
        Sql.Table<AgentSessionEntity>()
            .Where(s => s.Id == id)
            .ExecuteUpdate(set => set
                .SetProperty(s => s.TurnCount, s => s.TurnCount + 1)
                .SetProperty(s => s.TotalInputTokens, s => s.TotalInputTokens + s.TurnCount));

    private static int TransitionQuery(Guid id, AgentSessionState from, AgentSessionState to) =>
        Sql.Table<AgentSessionEntity>()
            .Where(s => s.Id == id && s.State == from)
            .ExecuteUpdate(set => set.SetProperty(s => s.State, to));

    private static int RecordOutcomeQuery(Guid id, string url, string? pullRequestUrl, string? validation) =>
        Sql.Table<AnalysisRequestEntity>()
            .Where(r => r.Id == id)
            .ExecuteUpdate(set => set
                .SetProperty(r => r.Repository.Url, url)
                .SetProperty(r => r.Outcome!.PullRequestUrl, pullRequestUrl)
                .SetProperty(r => r.Outcome!.ValidationResult, validation));

    private static IQuery<AgentSessionLine> SessionQuery(Guid id) =>
        Sql.Table<AgentSessionEntity>()
            .Where(s => s.Id == id)
            .Select(s => new AgentSessionLine(s.Id, s.State, s.LastActivityAt, s.TurnCount, s.TotalInputTokens, s.EndedAt));

    private static IQuery<OutcomeLine> OutcomeQuery(Guid id) =>
        Sql.Table<AnalysisRequestEntity>()
            .Where(r => r.Id == id)
            .Select(r => new OutcomeLine(r.Repository.Url, r.Outcome!.PullRequestUrl, r.Outcome!.ValidationResult));
}
