namespace ZeroAlloc.ORM.AotSmoke;

// The entity a [LinqQuery] chain reads through. It is a shape for the companion only; rows are
// read into LinqItemRow, so no LinqItem is ever constructed.
public sealed class LinqItem
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = "";

    public int Rank { get; private set; }

    public Priority Priority { get; private set; }

    public Status Status { get; private set; }
}
