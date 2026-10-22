namespace ZeroAlloc.ORM.Integration.Tests.Linq;

public sealed record AgentSessionLine(
    Guid Id,
    AgentSessionState State,
    DateTimeOffset LastActivityAt,
    int TurnCount,
    long TotalInputTokens,
    DateTimeOffset? EndedAt);
