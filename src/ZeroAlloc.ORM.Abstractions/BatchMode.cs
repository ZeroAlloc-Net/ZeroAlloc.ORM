namespace ZeroAlloc.ORM;

/// <summary>Multi-statement execution strategy for <see cref="QueryAttribute.Batch"/>.</summary>
public enum BatchMode
{
    Auto,
    Always,
    Never,
}
