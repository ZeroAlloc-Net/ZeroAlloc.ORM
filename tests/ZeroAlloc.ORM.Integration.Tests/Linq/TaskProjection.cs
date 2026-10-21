namespace ZeroAlloc.ORM.Integration.Tests.Linq;

internal sealed record TaskProjection(
    Guid Id,
    ProjectId ProjectId,
    WorkStatus Status,
    WorkKind Kind,
    string[] Tags,
    DateTimeOffset? DueAt);
