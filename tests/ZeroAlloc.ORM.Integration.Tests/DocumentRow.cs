namespace ZeroAlloc.ORM.Integration.Tests;

internal sealed record DocumentRow(Guid Id, [Json] string Body, [Json] string? Note);
