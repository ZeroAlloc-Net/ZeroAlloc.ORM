namespace ZeroAlloc.ORM;

/// <summary>
/// Materialization strategy values for <see cref="MaterializeAttribute.Strategy"/>.
/// v0.5 emits the <see cref="Custom"/> shape implicitly whenever
/// <see cref="MaterializeAttribute.Factory"/> is non-null; the other values are
/// reserved for future use (the generator currently ignores Strategy on its own).
/// </summary>
public enum MaterializeStrategy
{
    /// <summary>Default — generator picks via convention discovery.</summary>
    Auto,
    /// <summary>Force positional FlatRow shape.</summary>
    FlatRow,
    /// <summary>Force column-name-keyed DomainEntity shape.</summary>
    DomainEntity,
    /// <summary>Force user-supplied factory (use <see cref="MaterializeAttribute.Factory"/>).</summary>
    Custom,
}
