namespace ZeroAlloc.ORM.Linq;

/// <summary>
/// Marks a partial method whose SQL is generated from a LINQ chain. <see cref="Companion"/> names a
/// method in the same type that holds the chain; the companion is read at build time and never runs.
/// </summary>
/// <param name="companion">The name of the companion method that holds the LINQ chain.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LinqQueryAttribute(string companion) : Attribute
{
    /// <summary>Gets the name of the companion method that holds the LINQ chain.</summary>
    public string Companion { get; } = companion;
}
