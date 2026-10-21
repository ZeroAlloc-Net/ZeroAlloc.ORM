namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// One joined pair: an execution's input tokens next to its task's title.
internal sealed record ExecutionTitle(string Title, int InputTokens);
