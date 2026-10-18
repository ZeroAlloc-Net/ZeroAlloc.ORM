using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.TypeConversions;

/// <summary>
/// Discovery output for a single type. <see cref="Kind"/> is always populated; the other
/// fields are set when relevant to the kind (factory method for <see cref="ConventionKind.StaticFactory"/>,
/// value property for unwrap, etc.) and <c>null</c>/empty otherwise.
/// </summary>
/// <param name="Kind">Which convention rule matched.</param>
/// <param name="Factory">Method or ctor to invoke for construction, when applicable.</param>
/// <param name="Value">Property to read for unwrapping back to the primitive, when applicable.</param>
/// <param name="ExpandedColumns">Columns when the type is composite (reserved for multi-arg ctors).</param>
public sealed record ConventionResult(
    ConventionKind Kind,
    ISymbol? Factory,
    IPropertySymbol? Value,
    ImmutableArray<IParameterSymbol> ExpandedColumns)
{
    /// <summary>
    /// The element type of a <see cref="ConventionKind.PrimitiveArray"/>, for example
    /// <c>string</c> for <c>List&lt;string&gt;</c>; <c>null</c> for every other kind.
    /// It keeps the nullable annotation as written, so strip it with
    /// <c>WithNullableAnnotation(NullableAnnotation.NotAnnotated)</c> before rendering a type name.
    /// </summary>
    public ITypeSymbol? ElementType { get; init; }

    /// <summary>
    /// Which collection shape a <see cref="ConventionKind.PrimitiveArray"/> matched;
    /// <see cref="PrimitiveArrayShape.None"/> for every other kind.
    /// </summary>
    public PrimitiveArrayShape ArrayShape { get; init; }
}
