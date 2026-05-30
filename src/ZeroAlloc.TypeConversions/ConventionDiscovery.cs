using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.TypeConversions;

/// <summary>
/// Convention-discovery entry point: classifies an arbitrary <see cref="ITypeSymbol"/>
/// into a <see cref="ConventionResult"/> the generator (and future ZA.Mapping) can consume.
///
/// Rules are checked in priority order (see design doc Section 3); first match wins:
/// <list type="number">
///   <item>Explicit <c>[Materialize]</c> annotation (reserved for v0.3+).</item>
///   <item>Primitive (<see cref="PrimitiveCatalog"/>).</item>
///   <item>ZA.ValueObjects <c>[ValueObject]</c> attribute.</item>
///   <item>Static <c>From</c> / <c>FromValue</c> factory.</item>
///   <item>Single-arg record primary constructor.</item>
///   <item>Enum (and <c>[StoreAsString]</c> variant).</item>
///   <item>Multi-arg constructor (reserved for v0.5).</item>
///   <item><see cref="ConventionKind.Unknown"/>.</item>
/// </list>
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
        if (TryExplicitMaterialize(type) is { } m1) return m1;
        if (TryPrimitive(type) is { } m2) return m2;

        if (type is INamedTypeSymbol named)
        {
            if (TryValueObject(named) is { } m3) return m3;
            if (TryStaticFactory(named) is { } m4) return m4;
            if (TrySingleArgCtor(named, context) is { } m5) return m5;
            if (TryEnum(named) is { } m6) return m6;
            if (TryMultiArgCtor(named, context) is { } m7) return m7;
        }

        return UnknownResult;
    }

    private const string ValueObjectAttributeFqn = "ZeroAlloc.ValueObjects.ValueObjectAttribute";
    private const string StoreAsStringAttributeFqn = "ZeroAlloc.ORM.StoreAsStringAttribute";

    // Placeholder for v0.3's [Materialize] attribute. Returning null today keeps the
    // priority ladder cleanly ordered — when the attribute lands, only this method
    // changes; the Resolve dispatcher stays put.
    private static ConventionResult? TryExplicitMaterialize(ITypeSymbol type)
    {
        _ = type;
        return null;
    }

    private static ConventionResult? TryPrimitive(ITypeSymbol type)
    {
        if (!PrimitiveCatalog.IsPrimitive(type)) return null;
        return new ConventionResult(
            ConventionKind.Primitive,
            Factory: null,
            Value: null,
            ExpandedColumns: ImmutableArray<IParameterSymbol>.Empty);
    }

    // ZA.ValueObjects marks wrapper types with [ValueObject]. By convention the
    // generated type exposes `T Value { get; }` and `static T From(TPrim)`. We
    // surface those when present; when the generator hasn't run yet in a test
    // compilation we still return the ValueObject kind so callers can react to the
    // annotation alone. FQN match keeps this independent of an assembly reference.
    private static ConventionResult? TryValueObject(INamedTypeSymbol type)
    {
        if (!HasAttribute(type, ValueObjectAttributeFqn)) return null;

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

    // Hand-rolled wrapper types frequently expose `static T From(TPrim)` or
    // `static T FromValue(TPrim)` as the canonical factory. The factory often
    // encodes invariants the primary ctor doesn't, so when both are present we
    // prefer the factory — that's why this rule sits above SingleArgCtor.
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

        var valueProp = type.GetMembers("Value").OfType<IPropertySymbol>().FirstOrDefault();

        return new ConventionResult(
            ConventionKind.StaticFactory,
            Factory: factory,
            Value: valueProp,
            ExpandedColumns: ImmutableArray<IParameterSymbol>.Empty);
    }

    // Records with a single primary-ctor parameter that resolves to a primitive are
    // the idiomatic "lightweight value-object without ZA.ValueObjects" shape. Match
    // both `record class Foo(int X)` and `record struct Foo(int X)`.
    private static ConventionResult? TrySingleArgCtor(INamedTypeSymbol type, ConventionContext context)
    {
        _ = context;
        if (!type.IsRecord) return null;

        var ctor = type.InstanceConstructors.FirstOrDefault(c =>
            c.DeclaredAccessibility == Accessibility.Public &&
            c.Parameters.Length == 1);

        if (ctor is null) return null;
        if (!PrimitiveCatalog.IsPrimitive(ctor.Parameters[0].Type)) return null;

        var paramName = ctor.Parameters[0].Name;
        var valueProp = type.GetMembers(paramName).OfType<IPropertySymbol>().FirstOrDefault();

        return new ConventionResult(
            ConventionKind.SingleArgCtor,
            Factory: ctor,
            Value: valueProp,
            ExpandedColumns: ImmutableArray<IParameterSymbol>.Empty);
    }

    // Enums round-trip as their underlying integral type by default. The
    // [StoreAsString] opt-in marker (defined in ZA.ORM.Abstractions) flips that to
    // string round-trip so wire-format diffs survive enum reordering.
    private static ConventionResult? TryEnum(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Enum) return null;

        var kind = HasAttribute(type, StoreAsStringAttributeFqn)
            ? ConventionKind.EnumAsString
            : ConventionKind.Enum;

        return new ConventionResult(
            kind,
            Factory: null,
            Value: null,
            ExpandedColumns: ImmutableArray<IParameterSymbol>.Empty);
    }

    // Multi-arg composite shapes (DateRange(DateTime From, DateTime To), etc.) are
    // reserved for v0.5. The placeholder keeps the priority ladder symmetric.
    private static ConventionResult? TryMultiArgCtor(INamedTypeSymbol type, ConventionContext context)
    {
        _ = type;
        _ = context;
        return null;
    }

    private static bool HasAttribute(INamedTypeSymbol type, string fqn)
    {
        foreach (var attr in type.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            if (string.Equals(name, fqn, System.StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
