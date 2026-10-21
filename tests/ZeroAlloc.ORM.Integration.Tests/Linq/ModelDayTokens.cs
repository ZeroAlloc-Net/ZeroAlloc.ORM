namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Daedalus' Q1 row: one model's token use on one day.
internal sealed record ModelDayTokens(string? ModelId, DateTime Day, long InputTokens, long OutputTokens, int Executions);
