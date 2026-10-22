namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Daedalus' Q3 row: one project's token use with one model.
internal sealed record ProjectModelCost(Guid ProjectId, string ProjectName, string? ModelId, long InputTokens, long OutputTokens, int Executions);
