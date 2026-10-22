namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Two "Id" columns from two tables in one row, next to the project's renamed Title.
internal sealed record TaskProjectIds(Guid TaskId, Guid ProjectId, string ProjectName);
