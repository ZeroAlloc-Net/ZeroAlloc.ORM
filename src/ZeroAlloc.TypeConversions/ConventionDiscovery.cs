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

        if (type is INamedTypeSymbol named)
        {
            if (TryValueObject(named) is { } voResult) return voResult;
            if (TryStaticFactory(named) is { } facResult) return facResult;
            if (TrySingleArgCtor(named, context) is { } ctorResult) return ctorResult;
            if (TryEnum(named) is { } enumResult) return enumResult;
        }

        return UnknownResult;
    }

    private const string StoreAsStringAttributeFqn = "ZeroAlloc.ORM.StoreAsStringAttribute";

    // Enums round-trip as their underlying integral type by default. The
    // [StoreAsString] opt-in marker (defined in ZA.ORM.Abstractions) flips that to
    // string round-trip so wire-format diffs survive enum reordering. The attribute
    // match is FQN-based so the discovery layer doesn't depend on Abstractions.
    private static ConventionResult? TryEnum(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Enum) return null;

        var kind = HasStoreAsStringAttribute(type)
            ? ConventionKind.EnumAsString
            : ConventionKind.Enum;

        return new ConventionResult(
            kind,
            Factory: null,
            Value: null,
            ExpandedColumns: ImmutableArray<IParameterSymbol>.Empty);
    }

    private static bool HasStoreAsStringAttribute(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            if (string.Equals(name, StoreAsStringAttributeFqn, System.StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    // Hand-rolled wrapper types frequently expose `static T From(TPrim)` or
    // `static T FromValue(TPrim)` as the canonical factory (often because the ctor
    // is private to enforce invariants). v0.2 hard-codes the two names; once
    // [Materialize(Factory)] lands the catalog will be configurable.
    private static ConventionResult? TryStaticFactory(INamedTypeSymbol type)
    {
        var factory = type.GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m =>
                m.IsStatic &&
                m.DeclaredAccessibility == Accessibility.Public &&
                m.Parameters.Length == 1 &&
                (string.Equals(m.Name, "From", System.StringComparison.Ordinal) ||
                 string.Equals(m.Name, "FromValue", System.StringComparison.Ordinal)) &&
                SymbolEqualityComparer.Default.Equals(m.ReturnType, type) &&
                PrimitiveCatalog.IsPrimitive(m.Parameters[0].Type));

        if (factory is null) return null;

        // Conventional unwrap property is `Value`; absent is allowed (some wrappers
        // expose a method or a differently-named property — the generator can fall
        // back to reflection-free strategies once this surfaces in Phase C).
        var valueProp = type.GetMembers("Value").OfType<IPropertySymbol>().FirstOrDefault();

        return new ConventionResult(
            ConventionKind.StaticFactory,
            Factory: factory,
            Value: valueProp,
            ExpandedColumns: ImmutableArray<IParameterSymbol>.Empty);
    }

    // Records with a single primary-ctor parameter that resolves to a primitive are
    // the idiomatic "lightweight value-object without ZA.ValueObjects" shape. We
    // match `record class Foo(int X)` and `record struct Foo(int X)` alike — both
    // produce a public instance ctor with exactly one parameter.
    private static ConventionResult? TrySingleArgCtor(INamedTypeSymbol type, ConventionContext context)
    {
        if (!type.IsRecord) return null;

        var ctor = type.InstanceConstructors.FirstOrDefault(c =>
            c.DeclaredAccessibility == Accessibility.Public &&
            c.Parameters.Length == 1);

        if (ctor is null) return null;

        // Only declare SingleArgCtor when the inner type itself is primitive; nested
        // wrappers (record A(B b) where B is another VO) are out of scope for v0.2.
        var paramType = ctor.Parameters[0].Type;
        if (!PrimitiveCatalog.IsPrimitive(paramType)) return null;

        // Records synthesize a property named after the ctor parameter verbatim.
        var paramName = ctor.Parameters[0].Name;
        var valueProp = type.GetMembers(paramName).OfType<IPropertySymbol>().FirstOrDefault();

        return new ConventionResult(
            ConventionKind.SingleArgCtor,
            Factory: ctor,
            Value: valueProp,
            ExpandedColumns: ImmutableArray<IParameterSymbol>.Empty);
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
