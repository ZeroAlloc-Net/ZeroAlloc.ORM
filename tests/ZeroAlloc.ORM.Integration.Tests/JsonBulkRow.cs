namespace ZeroAlloc.ORM.Integration.Tests;

// Body uses the property target on a positional record, Note the plain parameter target.
internal sealed record JsonBulkRow(Guid Id, [property: Json] string Body, [Json] string? Note);
