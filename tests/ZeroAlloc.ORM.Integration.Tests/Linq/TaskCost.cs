namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Daedalus' Q4 row: one task's token use.
internal sealed record TaskCost(Guid TaskId, string Title, long InputTokens, int Executions);
