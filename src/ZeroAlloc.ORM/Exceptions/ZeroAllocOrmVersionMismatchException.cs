namespace ZeroAlloc.ORM;

// Sealed exception types — only the documented ctors are part of the API contract
// (parameterless and serialization ctors deliberately omitted; BinaryFormatter is deprecated).
#pragma warning disable CA1032, RCS1194
public sealed class ZeroAllocOrmVersionMismatchException : Exception
{
    public ZeroAllocOrmVersionMismatchException(string message) : base(message) { }
}
#pragma warning restore CA1032, RCS1194
