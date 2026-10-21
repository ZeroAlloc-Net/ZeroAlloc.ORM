namespace ZeroAlloc.ORM.AotSmoke;

// Shapes for the grouped-join and Average smoke; like LinqItem, never constructed.
public sealed class SmokeTask
{
    public Guid Id { get; private set; }

    public string Title { get; private set; } = "";
}
