namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// One execution with the task it belongs to, a child row of a paged parent list.
internal sealed record ExecutionOfTask(Guid Id, Guid TaskId, int InputTokens);
