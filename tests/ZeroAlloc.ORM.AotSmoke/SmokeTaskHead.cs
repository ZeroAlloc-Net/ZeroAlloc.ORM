namespace ZeroAlloc.ORM.AotSmoke;

// A task with its optional origin's members, which read NULL for a task without one.
internal sealed record SmokeTaskHead(Guid Id, string Title, string? Url, string? Branch);
