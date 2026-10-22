namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Daedalus' BrainstormSession, which EF loads with Include(s => s.Messages).
public sealed class BrainstormSessionEntity
{
    public Guid Id { get; private set; }
    public string Topic { get; private set; } = "";
}
