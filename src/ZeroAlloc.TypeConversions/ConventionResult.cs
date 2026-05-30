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
    ImmutableArray<IParameterSymbol> ExpandedColumns);
