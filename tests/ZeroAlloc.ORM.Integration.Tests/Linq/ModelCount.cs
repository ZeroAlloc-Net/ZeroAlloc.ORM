namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// One model's executions, with a nullable Sum that is 0 when every value is null.
internal sealed record ModelCount(string? ModelId, long Executions, int? Sequence);
