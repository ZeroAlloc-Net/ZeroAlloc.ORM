namespace ZeroAlloc.TypeConversions;

/// <summary>
/// The collection shape a <see cref="ConventionKind.PrimitiveArray"/> type matched. The
/// generator binds and reads each shape differently, so discovery records which one it saw.
/// </summary>
public enum PrimitiveArrayShape
{
    /// <summary>Not a <see cref="ConventionKind.PrimitiveArray"/>.</summary>
    None,

    /// <summary>A single-dimensional array, <c>T[]</c>.</summary>
    Array,

    /// <summary><c>System.Collections.Generic.List&lt;T&gt;</c>.</summary>
    List,

    /// <summary><c>System.Collections.Generic.IReadOnlyList&lt;T&gt;</c>.</summary>
    ReadOnlyList,
}
