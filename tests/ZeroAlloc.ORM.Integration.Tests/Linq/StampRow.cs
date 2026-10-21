namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// A column next to a projected parameter that has no DbType facet.
internal sealed record StampRow(Guid Id, DateTime? At);
