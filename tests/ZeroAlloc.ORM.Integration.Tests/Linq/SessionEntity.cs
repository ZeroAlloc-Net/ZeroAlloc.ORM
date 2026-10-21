namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Daedalus' chat session, with the tokens it used in total. CreatedAt is a DateTimeOffset, so
// the grouped report reads `.Date` of a DateTimeOffset column as a DateTime.
public sealed class SessionEntity
{
    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public long TotalInputTokens { get; private set; }
    public long TotalOutputTokens { get; private set; }
}
