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
    private const string ColumnMapAttributeFullName = "ZeroAlloc.ORM.ColumnMapAttribute";
    private const string Zao106 = "ZAO106";

    public static EntityMapReadResult Read(
        GeneratorAttributeSyntaxContext ctx,
        SymbolDisplayFormat typeDisplayFormat,
        CancellationToken ct)
    {
        var tableAttribute = ctx.Attributes[0];
        var tableSyntax = tableAttribute.ApplicationSyntaxReference?.GetSyntax(ct);
        var tableLocation = LocationInfo.From(tableSyntax?.GetLocation() ?? ctx.TargetNode.GetLocation());
        var filePath = tableSyntax?.SyntaxTree.FilePath ?? string.Empty;
        var position = tableSyntax?.SpanStart ?? 0;

        if (tableAttribute.ConstructorArguments.Length != 2
            || tableAttribute.ConstructorArguments[0].Value is not INamedTypeSymbol entity
            || entity.TypeKind == TypeKind.Error
            || tableAttribute.ConstructorArguments[1].Value is not string table)
        {
            // An unresolved entity or a missing table name is already a compile error.
            return new EntityMapReadResult(null, default, tableLocation, filePath, position);
        }

        var order = new List<string>();
        var properties = ReadableProperties(entity, order);
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var overrides = new Dictionary<string, (string Column, bool EnumAsString)>(StringComparer.Ordinal);

        foreach (var attribute in ctx.TargetSymbol.GetAttributes())
        {
            if (!string.Equals(
                    attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "global::" + ColumnMapAttributeFullName,
                    StringComparison.Ordinal)
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

            if (enumAsString && !IsEnum(property.Type))
            {
                diagnostics.Add(Invalid(
                    $"EnumAsString is set on '{propertyName}' of {entityName}, which is not an enum",
                    location));
                continue;
            }

            overrides[propertyName] = (column, enumAsString);
        }

        var columns = ImmutableArray.CreateBuilder<ColumnMapModel>(properties.Count);
        foreach (var name in order)
        {
            columns.Add(overrides.TryGetValue(name, out var o)
                ? new ColumnMapModel(name, o.Column, o.EnumAsString)
                : new ColumnMapModel(name, name, EnumAsString: false));
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
            tableLocation,
            filePath,
            position);
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
            var byPath = string.CompareOrdinal(a.FilePath, b.FilePath);
            return byPath != 0 ? byPath : a.Position.CompareTo(b.Position);
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

    // Public instance properties (names land in `order`, the entity's own first) with a public getter, from the entity and its base types. The
    // setter may have any accessibility: entities with private setters are mapped too. A derived
    // property hides a base one of the same name.
    private static Dictionary<string, IPropertySymbol> ReadableProperties(INamedTypeSymbol entity, List<string> order)
    {
        var result = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
        for (INamedTypeSymbol? type = entity; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers())
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
                    {
                        result.Add(property.Name, property);
                        order.Add(property.Name);
                    }
                }
            }
        }

        return result;
    }
}
