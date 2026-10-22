namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Daedalus' agent session, trimmed to what its stores update in place: its state, when it was
// last active, and the counters each turn adds to. Never constructed by the ORM.
public sealed class AgentSessionEntity
{
    public Guid Id { get; private set; }
    public AgentSessionState State { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public int TurnCount { get; private set; }
    public long TotalInputTokens { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
}
