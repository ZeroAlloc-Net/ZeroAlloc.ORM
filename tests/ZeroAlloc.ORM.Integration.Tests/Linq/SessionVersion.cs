namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// An agent session as a guarded write reads it: its turn count and the xmin it carries.
public sealed record SessionVersion(Guid Id, int TurnCount, uint Version);
