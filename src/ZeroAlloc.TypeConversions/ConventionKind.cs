namespace ZeroAlloc.TypeConversions;

/// <summary>
/// Classifies how a type is materialized from raw column data. Each kind corresponds to
/// a distinct construction strategy the generator emits: primitive scalar reads, enum
/// conversion, value-object factory invocation, single-arg record ctor, etc.
/// </summary>
public enum ConventionKind
{
    /// <summary>No discovery rule matched. The generator should emit a diagnostic.</summary>
    Unknown,

    /// <summary>Scalar primitive (int, string, Guid, byte[], etc.). Handled by <see cref="PrimitiveCatalog"/>.</summary>
    Primitive,

    /// <summary>An enum stored as its underlying integral type.</summary>
    Enum,

    /// <summary>An enum stored as its member name (string). Triggered by <c>[StoreAsString]</c>.</summary>
    EnumAsString,

    /// <summary>A type annotated with ZA.ValueObjects' <c>[ValueObject]</c> attribute.</summary>
    ValueObject,

    /// <summary>A type exposing a <c>static T From(TPrim)</c> or <c>FromValue</c> factory.</summary>
    StaticFactory,

    /// <summary>A record (struct or class) with a single-parameter primary constructor.</summary>
    SingleArgCtor,

    /// <summary>Multi-arg ctor / composite shape. Reserved for v0.5.</summary>
    MultiArgCtor,

    /// <summary>Explicit <c>[Materialize]</c> annotation overrides discovery. Reserved.</summary>
    ExplicitMaterialize,

    /// <summary>
    /// A PostgreSQL array: <c>T[]</c>, <c>List&lt;T&gt;</c> or <c>IReadOnlyList&lt;T&gt;</c> of
    /// <c>string</c>, <c>int</c>, <c>long</c> or <c>Guid</c>. <see cref="ConventionResult.ElementType"/>
    /// and <see cref="ConventionResult.ArrayShape"/> name the element and the collection shape.
    /// <c>byte[]</c> is a <see cref="Primitive"/>, never a PrimitiveArray.
    /// </summary>
    PrimitiveArray,
}
