namespace ZeroAlloc.ORM;

/// <summary>Materialization strategy selector for <see cref="MaterializeAttribute"/>.</summary>
public enum MaterializeStrategy
{
    /// <summary>Generator infers from the target type's shape.</summary>
    Auto,

    /// <summary>Treat the type as a flat row — bind columns positionally to ctor params / properties.</summary>
    FlatRow,

    /// <summary>Treat the type as a domain entity (nested objects, value objects, etc.).</summary>
    DomainEntity,

    /// <summary>Delegate to a user-supplied factory named by <see cref="MaterializeAttribute.Factory"/>.</summary>
    Custom,
}
