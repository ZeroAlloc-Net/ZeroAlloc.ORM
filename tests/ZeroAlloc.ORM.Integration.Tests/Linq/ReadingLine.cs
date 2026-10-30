namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// A reading's settable times, as read back.
internal sealed record ReadingLine(Guid Id, DateTime? ClosedAt, DateTime? CheckedAt);
