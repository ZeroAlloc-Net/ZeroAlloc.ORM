using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// Reads <c>[TableMap]</c> and <c>[ColumnMap]</c> from a map class into an <see cref="EntityMapModel"/>
/// and reports ZAO106 for an invalid map.
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
        var overrides = new Dictionary<string, (string Column, bool EnumAsString)>(StringComparer.Ordinal);
        var overrideLocations = new Dictionary<string, LocationInfo?>(StringComparer.Ordinal);

        foreach (var attribute in ctx.TargetSymbol.GetAttributes())
        {
            if (!IsColumnMap(attribute.AttributeClass)
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0].Value is not string propertyName)
            {
                continue;
            }

            var location = LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation());
            var entityName = entity.ToDisplayString();

            if (!properties.TryGetValue(propertyName, out var property))
            {
                diagnostics.Add(Invalid(
                    $"[ColumnMap] names '{propertyName}', which is not a public readable property of {entityName}",
                    location));
                continue;
            }

            if (overrides.ContainsKey(propertyName))
            {
                diagnostics.Add(Invalid($"property '{propertyName}' of {entityName} is mapped twice", location));
                continue;
            }

            var column = propertyName;
            var enumAsString = false;
            foreach (var named in attribute.NamedArguments)
            {
                if (string.Equals(named.Key, "Column", StringComparison.Ordinal) && named.Value.Value is string c)
                    column = c;
                else if (string.Equals(named.Key, "EnumAsString", StringComparison.Ordinal) && named.Value.Value is bool b)
                    enumAsString = b;
            }

            overrides[propertyName] = (column, enumAsString && IsEnum(property.Type));
            overrideLocations[propertyName] = location;

            if (enumAsString && !IsEnum(property.Type))
            {
                diagnostics.Add(Invalid(
                    $"EnumAsString is set on '{propertyName}' of {entityName}, which is not an enum",
                    location));
            }
        }

        var columns = ImmutableArray.CreateBuilder<ColumnMapModel>(properties.Count);
        foreach (var name in order)
        {
            columns.Add(overrides.TryGetValue(name, out var o)
                ? new ColumnMapModel(name, o.Column, o.EnumAsString)
                : new ColumnMapModel(name, name, EnumAsString: false));
        }

        // Two properties may not share a column. At least one of the pair carries an override, since
        // property names are unique; the later property's override is the one reported, else the earlier's.
        var columnOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            if (!columnOwners.TryGetValue(column.Column, out var owner))
            {
                columnOwners.Add(column.Column, column.Property);
                continue;
            }

            if (!overrideLocations.TryGetValue(column.Property, out var at)
                && !overrideLocations.TryGetValue(owner, out at))
            {
                at = tableLocation;
            }

            diagnostics.Add(Invalid(
                $"properties '{owner}' and '{column.Property}' of {entity.ToDisplayString()} both map to column '{column.Column}'",
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

    /// <summary>
    /// Orders the maps by file path then position, keeps the first map of each entity and reports
    /// ZAO106 at the table attribute of every later one.
    /// </summary>
    public static EntityMapSet Resolve(ImmutableArray<EntityMapReadResult> results)
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
                maps.Add(model);
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

    // Matches ZeroAlloc.ORM.ColumnMapAttribute by namespace and metadata name.
    private static bool IsColumnMap(INamedTypeSymbol? type) =>
        type is { MetadataName: "ColumnMapAttribute", ContainingNamespace: { Name: "ORM", ContainingNamespace: { Name: "ZeroAlloc", ContainingNamespace.IsGlobalNamespace: true } } };

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
    // entity, each in declaration order; `order` receives the names in that order. The setter may
    // have any accessibility: entities with private setters are mapped too. A property that hides
    // or overrides a base one of the same name replaces it and keeps the base position.
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
}
