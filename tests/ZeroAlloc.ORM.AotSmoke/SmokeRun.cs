namespace ZeroAlloc.ORM.AotSmoke;

public sealed class SmokeRun
{
    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public int Tokens { get; private set; }
}
