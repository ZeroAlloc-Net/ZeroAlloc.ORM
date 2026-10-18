namespace ZeroAlloc.ORM.Integration.Tests;

internal sealed record SqliteDocumentRow(long Id, [Json] string Body, [Json] string? Note);
