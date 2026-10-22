namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// One message of a brainstorm session, read in CreatedAt order.
public sealed class BrainstormMessageEntity
{
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public string Role { get; private set; } = "";
    public string Content { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
}
