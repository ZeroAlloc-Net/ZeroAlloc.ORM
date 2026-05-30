namespace ZeroAlloc.ORM;

// Sealed exception types — only the documented ctors are part of the API contract
// (parameterless and serialization ctors deliberately omitted; BinaryFormatter is deprecated).
#pragma warning disable CA1032, RCS1194
public sealed class ZeroAllocOrmMaterializationException : Exception
{
    public ZeroAllocOrmMaterializationException(string message) : base(message) { }

    public ZeroAllocOrmMaterializationException(string message, Exception inner) : base(message, inner) { }
}
#pragma warning restore CA1032, RCS1194
