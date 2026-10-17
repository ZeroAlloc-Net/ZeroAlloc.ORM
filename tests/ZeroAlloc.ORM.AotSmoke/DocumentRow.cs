namespace ZeroAlloc.ORM.AotSmoke;

internal sealed record DocumentRow(Guid Id, [Json] string Body, [Json] string? Note);
