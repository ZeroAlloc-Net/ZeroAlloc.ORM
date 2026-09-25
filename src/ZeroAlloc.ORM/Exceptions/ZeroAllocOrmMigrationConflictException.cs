namespace ZeroAlloc.ORM;

// Sealed exception types — symmetrical 3-ctor set (parameterless + message + message/inner).
// Serialization ctors deliberately omitted; BinaryFormatter is deprecated.
#pragma warning disable CA1032, RCS1194
public sealed class ZeroAllocOrmMigrationConflictException : Exception
{
    public ZeroAllocOrmMigrationConflictException() { }

    public ZeroAllocOrmMigrationConflictException(string message) : base(message) { }

    public ZeroAllocOrmMigrationConflictException(string message, Exception inner) : base(message, inner) { }
}
#pragma warning restore CA1032, RCS1194
