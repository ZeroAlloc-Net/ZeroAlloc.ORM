namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Daedalus' Q2 row: one owner's session tokens on one day.
internal sealed record OwnerDayTokens(string OwnerId, DateTime Day, long InputTokens, long OutputTokens, int Sessions);
