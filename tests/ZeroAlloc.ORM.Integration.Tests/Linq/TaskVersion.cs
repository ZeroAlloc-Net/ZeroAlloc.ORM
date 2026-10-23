namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// A task with its RowVersion, the xmin its map names.
public sealed record TaskVersion(Guid Id, string Title, uint RowVersion);
