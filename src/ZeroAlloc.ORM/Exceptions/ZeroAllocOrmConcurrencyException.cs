namespace ZeroAlloc.ORM;

/// <summary>
/// Thrown by a <c>[LinqCommand(ThrowOnNoRows = true)]</c> method whose write affected no rows: the
/// row it guards was changed or removed since it was read.
/// </summary>
public sealed class ZeroAllocOrmConcurrencyException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ZeroAllocOrmConcurrencyException"/> class.</summary>
    public ZeroAllocOrmConcurrencyException() { }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The message, which names the method.</param>
    public ZeroAllocOrmConcurrencyException(string message) : base(message) { }

    /// <summary>Initializes a new instance with a message and the exception that caused it.</summary>
    /// <param name="message">The message.</param>
    /// <param name="inner">The exception that caused this one.</param>
    public ZeroAllocOrmConcurrencyException(string message, Exception inner) : base(message, inner) { }
}
