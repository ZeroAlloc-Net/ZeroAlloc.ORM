using System.Collections.Immutable;
using System.Linq;
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

        if (type is INamedTypeSymbol named && TryValueObject(named) is { } voResult)
        {
            return voResult;
        }

        return UnknownResult;
    }

    private const string ValueObjectAttributeFqn = "ZeroAlloc.ValueObjects.ValueObjectAttribute";

    // ZA.ValueObjects marks wrapper types with [ValueObject]. By convention the
    // generated type exposes `T Value { get; }` (the wrapped primitive) and
    // `static T From(TPrim)` (the canonical factory). We surface those when present;
    // when the generator hasn't run yet in a test compilation, we still return the
    // ValueObject kind so callers can react to the annotation alone.
    private static ConventionResult? TryValueObject(INamedTypeSymbol type)
    {
        if (!HasValueObjectAttribute(type))
        {
            return null;
        }

        var valueProp = type.GetMembers("Value").OfType<IPropertySymbol>().FirstOrDefault();
        var factory = type.GetMembers("From")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);

        return new ConventionResult(
            ConventionKind.ValueObject,
            Factory: factory,
            Value: valueProp,
            ExpandedColumns: ImmutableArray<IParameterSymbol>.Empty);
    }

    private static bool HasValueObjectAttribute(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            if (string.Equals(name, ValueObjectAttributeFqn, System.StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
