using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using ZeroAlloc.ORM.Generator.Model;
using ZeroAlloc.TypeConversions;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// Reads <c>[TableMap]</c>, <c>[OwnedMap]</c> and <c>[ColumnMap]</c> from a map class into an
/// <see cref="EntityMapModel"/> and reports ZAO106 for an invalid map.
/// </summary>
internal static class EntityMapReader
{
    private const string Zao106 = "ZAO106";

    public static EntityMapReadResult Read(
        GeneratorAttributeSyntaxContext ctx,
        SymbolDisplayFormat typeDisplayFormat,
        CancellationToken ct)
    {
        var tableAttribute = ctx.Attributes[0];
        var tableSyntax = tableAttribute.ApplicationSyntaxReference?.GetSyntax(ct);
        var tableLocation = LocationInfo.From(tableSyntax?.GetLocation() ?? ctx.TargetNode.GetLocation());

        if (tableAttribute.ConstructorArguments.Length != 2
            || tableAttribute.ConstructorArguments[0].Value is not INamedTypeSymbol entity
            || entity.TypeKind == TypeKind.Error
            || tableAttribute.ConstructorArguments[1].Value is not string table)
        {
            // An unresolved entity or a missing table name is already a compile error.
            return new EntityMapReadResult(null, default, tableLocation);
        }

        var order = new List<string>();
        var properties = ReadableProperties(entity, order);
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var overrides = new Dictionary<string, (string Column, bool EnumAsString, bool Ignore, DateTimeColumnStorage DateTimeStorage)>(StringComparer.Ordinal);
        var overrideLocations = new Dictionary<string, LocationInfo?>(StringComparer.Ordinal);
        var entityName = entity.ToDisplayString();
        var rejectedOwners = new HashSet<string>(StringComparer.Ordinal);
        var owned = ReadOwned(ctx, entityName, properties, diagnostics, rejectedOwners, ct);

        foreach (var attribute in ctx.TargetSymbol.GetAttributes())
        {
            if (!IsColumnMap(attribute.AttributeClass)
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0].Value is not string propertyName)
            {
                continue;
            }

            var location = LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation());
            if (ResolveColumnMapTarget(propertyName, entityName, properties, owned, rejectedOwners, location, diagnostics) is not { } property)
                continue;

            if (overrides.ContainsKey(propertyName))
            {
                diagnostics.Add(Invalid($"property '{propertyName}' of {entityName} is mapped twice", location));
                continue;
            }

            // A member of an owned object defaults to EF Core's {Owner}_{Member}.
            var column = propertyName.Replace('.', '_');
            var enumAsString = false;
            var ignore = false;
            var storageSet = false;
            var dateTimeStorage = 0;
            foreach (var named in attribute.NamedArguments)
            {
                if (string.Equals(named.Key, "Column", StringComparison.Ordinal))
                {
                    storageSet = true;
                    if (named.Value.Value is string c)
                        column = c;
                }
                else if (string.Equals(named.Key, "EnumAsString", StringComparison.Ordinal))
                {
                    storageSet = true;
                    enumAsString = named.Value.Value is true;
                }
                else if (string.Equals(named.Key, "Ignore", StringComparison.Ordinal))
                {
                    ignore = named.Value.Value is true;
                }
                else if (string.Equals(named.Key, "DateTimeStorage", StringComparison.Ordinal))
                {
                    storageSet = true;
                    dateTimeStorage = named.Value.Value is int value ? value : 0;
                }
            }

            var isDateTime = IsDateTime(property.Type);
            overrides[propertyName] = (
                column,
                enumAsString && IsEnum(property.Type),
                ignore,
                isDateTime ? StorageOf(dateTimeStorage) : DateTimeColumnStorage.None);
            overrideLocations[propertyName] = location;

            if (ignore)
            {
                // An ignored property has no column, so how it is stored means nothing.
                if (storageSet)
                {
                    diagnostics.Add(Invalid(
                        $"Ignore is set on '{propertyName}' of {entityName} together with Column, EnumAsString or DateTimeStorage; an ignored property has no column",
                        location));
                }
            }
            else
            {
                if (enumAsString && !IsEnum(property.Type))
                {
                    diagnostics.Add(Invalid(
                        $"EnumAsString is set on '{propertyName}' of {entityName}, which is not an enum",
                        location));
                }

                if (dateTimeStorage is < 0 or > 2)
                {
                    diagnostics.Add(Invalid(
                        $"DateTimeStorage on '{propertyName}' of {entityName} is {dateTimeStorage}, which DateTimeStorage does not declare",
                        location));
                }
                else if (dateTimeStorage != 0 && !isDateTime)
                {
                    diagnostics.Add(Invalid(
                        $"DateTimeStorage is set on '{propertyName}' of {entityName}, which is not a DateTime",
                        location));
                }
            }
        }

        // An owned property is no column itself: its members are, in their declaration order, at
        // its position, each by its path, Owner.Member, under the same rules.
        var columns = ImmutableArray.CreateBuilder<ColumnMapModel>(properties.Count);
        foreach (var name in order)
        {
            if (owned.TryGetValue(name, out var ownedType))
            {
                foreach (var memberName in ownedType.Order)
                {
                    var member = ownedType.Properties[memberName];
                    AddColumn(columns, overrides, owner: name, memberName, member, name + "_" + memberName);
                }
            }
            else
            {
                AddColumn(columns, overrides, owner: null, name, properties[name], name);
            }
        }

        // An owned object without a column has nothing to read, and `r.Outcome == null` nothing to test.
        foreach (var pair in owned)
        {
            if (!columns.Any(c => string.Equals(c.Owner, pair.Key, StringComparison.Ordinal)))
            {
                diagnostics.Add(Invalid(
                    $"'{pair.Key}' of {entityName} is declared owned, but none of its properties is a column",
                    pair.Value.Location));
            }
        }

        // Two properties may not share a column. At least one of the pair carries an override, since
        // property names are unique; the later property's override is the one reported, else the earlier's.
        var columnOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            if (!columnOwners.TryGetValue(column.Column, out var owner))
            {
                columnOwners.Add(column.Column, column.Path);
                continue;
            }

            if (!overrideLocations.TryGetValue(column.Path, out var at)
                && !overrideLocations.TryGetValue(owner, out at))
            {
                at = tableLocation;
            }

            diagnostics.Add(Invalid(
                $"properties '{owner}' and '{column.Path}' of {entityName} both map to column '{column.Column}'",
                at));
        }

        var schema = FindNamed(tableAttribute, "Schema") as string;
        var model = new EntityMapModel(
            entity.ToDisplayString(typeDisplayFormat),
            table,
            schema,
            new EquatableArray<ColumnMapModel>(columns.ToImmutable()));

        return new EntityMapReadResult(
            model,
            new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutable()),
            tableLocation);
    }

    // A [ColumnMap] decides: Ignore leaves the property out, anything else makes it a column. A
    // property without one is a column when it holds a value.
    private static void AddColumn(
        ImmutableArray<ColumnMapModel>.Builder columns,
        Dictionary<string, (string Column, bool EnumAsString, bool Ignore, DateTimeColumnStorage DateTimeStorage)> overrides,
        string? owner,
        string name,
        IPropertySymbol property,
        string defaultColumn)
    {
        var path = owner is null ? name : owner + "." + name;
        if (overrides.TryGetValue(path, out var o))
        {
            if (!o.Ignore)
                columns.Add(new ColumnMapModel(name, o.Column, o.EnumAsString, o.DateTimeStorage, owner));
        }
        else if (IsStored(property))
        {
            var storage = IsDateTime(property.Type) ? DateTimeColumnStorage.Default : DateTimeColumnStorage.None;
            columns.Add(new ColumnMapModel(name, defaultColumn, EnumAsString: false, storage, owner));
        }
    }

    /// <summary>
    /// One owned property's type: its readable properties, base type first, and where its
    /// <c>[OwnedMap]</c> is.
    /// </summary>
    private sealed class OwnedType(Dictionary<string, IPropertySymbol> properties, List<string> order, LocationInfo? location)
    {
        public Dictionary<string, IPropertySymbol> Properties { get; } = properties;

        public List<string> Order { get; } = order;

        public LocationInfo? Location { get; } = location;
    }

    // The map's [OwnedMap] properties, by name, each with its type's readable properties. A name
    // that is no property, a type with no members to map, or a second declaration is ZAO106. The
    // names of the first two go into `rejected`, so a dotted [ColumnMap] on them adds no second error.
    private static Dictionary<string, OwnedType> ReadOwned(
        GeneratorAttributeSyntaxContext ctx,
        string entityName,
        Dictionary<string, IPropertySymbol> properties,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        HashSet<string> rejected,
        CancellationToken ct)
    {
        var owned = new Dictionary<string, OwnedType>(StringComparer.Ordinal);
        ConventionContext? conventions = null;
        foreach (var attribute in ctx.TargetSymbol.GetAttributes())
        {
            if (!IsOwnedMap(attribute.AttributeClass)
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0].Value is not string propertyName)
            {
                continue;
            }

            var location = LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation());
            if (!properties.TryGetValue(propertyName, out var property))
            {
                diagnostics.Add(Invalid(
                    $"[OwnedMap] names '{propertyName}', which is not a public readable instance property of {entityName}",
                    location));
                rejected.Add(propertyName);
                continue;
            }

            conventions ??= new ConventionContext(ctx.SemanticModel.Compilation);
            if (!IsOwnable(property.Type, conventions) || property.Type is not INamedTypeSymbol ownedType)
            {
                diagnostics.Add(Invalid(
                    $"[OwnedMap] names '{propertyName}' of {entityName}, whose type '{property.Type.ToDisplayString()}' "
                        + "is a single value, an enum or a collection rather than a type whose properties are columns",
                    location));
                rejected.Add(propertyName);
                continue;
            }

            if (owned.ContainsKey(propertyName))
            {
                diagnostics.Add(Invalid($"property '{propertyName}' of {entityName} is declared owned twice", location));
                continue;
            }

            var order = new List<string>();
            owned[propertyName] = new OwnedType(ReadableProperties(ownedType, order), order, location);
        }

        return owned;
    }

    // The property a [ColumnMap] names: one of the entity, or a member of an owned property by its
    // dotted path, Owner.Member. Null after ZAO106 for a name or path that resolves to none, and for
    // the owned property itself, whose members are its columns.
    private static IPropertySymbol? ResolveColumnMapTarget(
        string path,
        string entityName,
        Dictionary<string, IPropertySymbol> properties,
        Dictionary<string, OwnedType> owned,
        HashSet<string> rejectedOwners,
        LocationInfo? location,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var segments = path.Split('.');
        if (segments.Length == 1)
        {
            if (!properties.TryGetValue(path, out var property))
            {
                diagnostics.Add(Invalid(
                    $"[ColumnMap] names '{path}', which is not a public readable instance property of {entityName}",
                    location));
                return null;
            }

            if (owned.ContainsKey(path))
            {
                diagnostics.Add(Invalid(
                    $"[ColumnMap] names '{path}' of {entityName}, which is declared owned; map each member by its path, such as '{path}.Member'",
                    location));
                return null;
            }

            return property;
        }

        if (segments.Length > 2)
        {
            diagnostics.Add(Invalid(
                $"[ColumnMap] names '{path}', a path deeper than Owner.Member, which is not supported",
                location));
            return null;
        }

        // Its [OwnedMap] is already reported.
        if (rejectedOwners.Contains(segments[0])) return null;

        if (!owned.TryGetValue(segments[0], out var ownedType))
        {
            diagnostics.Add(Invalid(
                $"[ColumnMap] names '{path}', but '{segments[0]}' of {entityName} is not declared with [OwnedMap]",
                location));
            return null;
        }

        if (!ownedType.Properties.TryGetValue(segments[1], out var member))
        {
            diagnostics.Add(Invalid(
                $"[ColumnMap] names '{path}', and '{segments[1]}' is not a public readable instance property of the owned type",
                location));
            return null;
        }

        return member;
    }

    /// <summary>
    /// Whether a property of type <paramref name="type"/> can be an owned object: a class or struct
    /// whose properties are columns. A type that binds as one value is not, nor is an enum, a
    /// nullable value type or a collection: a primitive, a value object with a <c>Value</c>, a
    /// <c>From</c> factory or a single-argument record.
    /// </summary>
    internal static bool IsOwnable(ITypeSymbol type, ConventionContext conventions)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct, SpecialType: SpecialType.None } named
            || named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return false;
        }

        foreach (var implemented in named.AllInterfaces)
        {
            if (implemented.SpecialType == SpecialType.System_Collections_IEnumerable) return false;
        }

        var convention = ConventionDiscovery.Resolve(named, conventions);
        return convention.Kind switch
        {
            ConventionKind.Unknown or ConventionKind.MultiArgCtor => true,
            ConventionKind.ValueObject => convention.Value is null,
            _ => false,
        };
    }

    /// <summary>
    /// The DateTime storage an <c>[assembly: EntityMapDefaults]</c> attribute declares, as the storage
    /// a DateTime column without its own setting takes: <see cref="DateTimeColumnStorage.Default"/>
    /// when it sets none.
    /// </summary>
    public static DateTimeColumnStorage ReadDefaultDateTimeStorage(GeneratorAttributeSyntaxContext ctx)
    {
        foreach (var attribute in ctx.Attributes)
        {
            if (FindNamed(attribute, "DateTimeStorage") is int value)
                return StorageOf(value);
        }

        return DateTimeColumnStorage.Default;
    }

    /// <summary>
    /// Orders the maps by file path then position, keeps the first map of each entity and reports
    /// ZAO106 at the table attribute of every later one. A DateTime column that declares no storage
    /// of its own takes <paramref name="defaultDateTimeStorage"/>.
    /// </summary>
    public static EntityMapSet Resolve(
        ImmutableArray<EntityMapReadResult> results,
        DateTimeColumnStorage defaultDateTimeStorage)
    {
        var ordered = new List<EntityMapReadResult>(results);
        ordered.Sort(static (a, b) =>
        {
            var byPath = string.CompareOrdinal(
                a.TableMapLocation?.Tree.FilePath ?? string.Empty,
                b.TableMapLocation?.Tree.FilePath ?? string.Empty);
            return byPath != 0
                ? byPath
                : (a.TableMapLocation?.Span.Start ?? 0).CompareTo(b.TableMapLocation?.Span.Start ?? 0);
        });

        var maps = ImmutableArray.CreateBuilder<EntityMapModel>();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var result in ordered)
        {
            foreach (var diagnostic in result.Diagnostics)
                diagnostics.Add(diagnostic);

            if (result.Model is not { } model)
                continue;

            if (seen.Add(model.EntityTypeFullName))
            {
                maps.Add(WithDefaultDateTimeStorage(model, defaultDateTimeStorage));
            }
            else
            {
                diagnostics.Add(Invalid(
                    $"{model.EntityTypeFullName} already has a [TableMap]; an entity is mapped once",
                    result.TableMapLocation));
            }
        }

        return new EntityMapSet(
            new EquatableArray<EntityMapModel>(maps.ToImmutable()),
            new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutable()));
    }

    // A DateTime column that declares no storage takes the assembly default; the model is kept
    // as it is when no column changes.
    private static EntityMapModel WithDefaultDateTimeStorage(EntityMapModel model, DateTimeColumnStorage storage)
    {
        if (storage == DateTimeColumnStorage.Default) return model;

        var changed = false;
        var columns = ImmutableArray.CreateBuilder<ColumnMapModel>(model.Columns.Count);
        foreach (var column in model.Columns)
        {
            if (column.DateTimeStorage == DateTimeColumnStorage.Default)
            {
                columns.Add(column with { DateTimeStorage = storage });
                changed = true;
            }
            else
            {
                columns.Add(column);
            }
        }

        return changed ? model with { Columns = new EquatableArray<ColumnMapModel>(columns.MoveToImmutable()) } : model;
    }

    // ZeroAlloc.ORM.DateTimeStorage's value as a DateTime column's storage. A value the enum does
    // not declare counts as Default; on a [ColumnMap] it is also ZAO106.
    private static DateTimeColumnStorage StorageOf(int value) => value switch
    {
        1 => DateTimeColumnStorage.Timestamp,
        2 => DateTimeColumnStorage.TimestampTz,
        _ => DateTimeColumnStorage.Default,
    };

    // Matches ZeroAlloc.ORM.ColumnMapAttribute by namespace and metadata name.
    private static bool IsColumnMap(INamedTypeSymbol? type) =>
        type is { MetadataName: "ColumnMapAttribute", ContainingNamespace: { Name: "ORM", ContainingNamespace: { Name: "ZeroAlloc", ContainingNamespace.IsGlobalNamespace: true } } };

    // Matches ZeroAlloc.ORM.OwnedMapAttribute by namespace and metadata name.
    private static bool IsOwnedMap(INamedTypeSymbol? type) =>
        type is { MetadataName: "OwnedMapAttribute", ContainingNamespace: { Name: "ORM", ContainingNamespace: { Name: "ZeroAlloc", ContainingNamespace.IsGlobalNamespace: true } } };

    private static bool IsDateTime(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_DateTime
        || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            && nullable.TypeArguments[0].SpecialType == SpecialType.System_DateTime;

    private static DiagnosticInfo Invalid(string reason, LocationInfo? location) =>
        new(Zao106, location, new EquatableArray<string>(ImmutableArray.Create(reason)));

    private static object? FindNamed(AttributeData attribute, string name)
    {
        foreach (var named in attribute.NamedArguments)
        {
            if (string.Equals(named.Key, name, StringComparison.Ordinal))
                return named.Value.Value;
        }

        return null;
    }

    private static bool IsEnum(ITypeSymbol type) =>
        type.TypeKind == TypeKind.Enum
        || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            && nullable.TypeArguments[0].TypeKind == TypeKind.Enum;

    // Public instance properties with a public getter, from the base types first and then the
    // entity, each in declaration order; `order` receives the names in that order. A property that
    // hides or overrides a base one of the same name replaces it and keeps the base position.
    // Which of them are columns is decided by IsStored and the map's [ColumnMap] attributes.
    private static Dictionary<string, IPropertySymbol> ReadableProperties(INamedTypeSymbol entity, List<string> order)
    {
        var chain = new List<INamedTypeSymbol>();
        for (INamedTypeSymbol? type = entity; type is not null; type = type.BaseType)
            chain.Add(type);

        var result = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            foreach (var member in chain[i].GetMembers())
            {
                if (member is IPropertySymbol
                    {
                        IsStatic: false,
                        IsIndexer: false,
                        DeclaredAccessibility: Accessibility.Public,
                        GetMethod.DeclaredAccessibility: Accessibility.Public,
                    } property)
                {
                    if (!result.ContainsKey(property.Name))
                        order.Add(property.Name);
                    result[property.Name] = property;
                }
            }
        }

        return result;
    }

    // Whether the property holds a value: it, or a property it overrides, has a visible setter of
    // any accessibility, init included, or is an auto-property. In source every setter is visible
    // and an auto-property has a backing field, so a computed `Label => First + "!"` has neither.
    // Metadata imports neither a private setter nor the private backing field, so there a get-only
    // property counts only when its getter is compiler generated: the compiler marks an
    // auto-property's accessors so, and not a hand-written getter.
    private static bool IsStored(IPropertySymbol property)
    {
        for (var p = property; p is not null; p = p.OverriddenProperty)
        {
            if (p.SetMethod is not null) return true;
            if (p.DeclaringSyntaxReferences.IsEmpty ? IsCompilerGenerated(p.GetMethod) : HasBackingField(p)) return true;
        }
        return false;
    }

    private static bool IsCompilerGenerated(IMethodSymbol? method)
    {
        if (method is null) return false;
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass is
                {
                    MetadataName: "CompilerGeneratedAttribute",
                    ContainingNamespace:
                    {
                        Name: "CompilerServices",
                        ContainingNamespace:
                        {
                            Name: "Runtime",
                            ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true },
                        },
                    },
                })
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasBackingField(IPropertySymbol property)
    {
        foreach (var member in property.ContainingType.GetMembers())
        {
            if (member is IFieldSymbol { IsImplicitlyDeclared: true } field
                && SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property))
            {
                return true;
            }
        }
        return false;
    }
}
