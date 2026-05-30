using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.TypeConversions;

/// <summary>
/// Convention-discovery entry point: classifies an arbitrary <see cref="ITypeSymbol"/>
/// into a <see cref="ConventionResult"/> the generator (and future ZA.Mapping) can consume.
///
/// Rules are checked in priority order (see design doc Section 3); first match wins.
/// </summary>
public static class ConventionDiscovery
{
    private static readonly ConventionResult UnknownResult = new(
        ConventionKind.Unknown,
        Factory: null,
        Value: null,
        ExpandedColumns: ImmutableArray<IParameterSymbol>.Empty);

    /// <summary>
    /// Classify <paramref name="type"/> for materialization. Returns
    /// <see cref="ConventionKind.Unknown"/> when no rule matches.
    /// </summary>
    /// <param name="type">The type to classify (a result column, parameter, or property type).</param>
    /// <param name="context">Compilation-scoped lookups for attributes and well-known types.</param>
    public static ConventionResult Resolve(ITypeSymbol type, ConventionContext context)
    {
        _ = context;

        if (PrimitiveCatalog.IsPrimitive(type))
        {
            return new ConventionResult(
                ConventionKind.Primitive,
                Factory: null,
                Value: null,
                ExpandedColumns: ImmutableArray<IParameterSymbol>.Empty);
        }

        return UnknownResult;
    }
}
