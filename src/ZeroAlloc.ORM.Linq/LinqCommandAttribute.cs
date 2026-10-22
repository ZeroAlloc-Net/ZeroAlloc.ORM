namespace ZeroAlloc.ORM.Linq;

/// <summary>
/// Marks a partial method whose SQL is a set-based write generated from a LINQ chain.
/// <see cref="Companion"/> names a method in the same type that holds the chain, which ends in
/// <c>ExecuteDelete()</c>; the companion is read at build time and never runs. The method returns
/// <c>Task</c> or <c>ValueTask</c>, or <c>Task&lt;int&gt;</c> or <c>ValueTask&lt;int&gt;</c> for the
/// number of rows affected.
/// </summary>
/// <param name="companion">The name of the companion method that holds the LINQ chain.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LinqCommandAttribute(string companion) : Attribute
{
    /// <summary>Gets the name of the companion method that holds the LINQ chain.</summary>
    public string Companion { get; } = companion;

    /// <summary>
    /// Gets a value indicating whether a write that affects no rows throws
    /// <c>ZeroAllocOrmConcurrencyException</c>: the row it guards was changed or removed since it
    /// was read. Off by default, when the method returns the number of rows affected, 0 included.
    /// </summary>
    public bool ThrowOnNoRows { get; init; }
}
