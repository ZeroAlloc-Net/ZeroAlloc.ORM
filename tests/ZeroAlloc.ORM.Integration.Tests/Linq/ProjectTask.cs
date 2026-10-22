namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// One task with the project it belongs to, a child row of a paged project list.
internal sealed record ProjectTask(Guid Id, Guid ProjectId, string Title);
