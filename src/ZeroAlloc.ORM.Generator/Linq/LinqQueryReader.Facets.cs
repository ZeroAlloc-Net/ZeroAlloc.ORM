using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using Microsoft.CodeAnalysis;
using ZeroAlloc.ORM.Generator.Model;
using ZeroAlloc.TypeConversions;

namespace ZeroAlloc.ORM.Generator.Linq;

internal static partial class LinqQueryReader
{
    private const string DbTypePrefix = "global::System.Data.DbType.";

    // The primitives a value parameter may bind as that get a DbType facet. DateTime is left
    // out, so Npgsql picks timestamp or timestamptz from its Kind, and so are arrays, which
    // Npgsql types from the value; so is every primitive the facet table does not name.
    private static readonly HashSet<string> FacetReaders = new(StringComparer.Ordinal)
    {
        "GetInt32",
        "GetInt64",
        "GetInt16",
        "GetBoolean",
        "GetGuid",
        "GetString",
        "GetDecimal",
        "GetDouble",
        "GetFloat",
        "GetFieldValue<global::System.DateTimeOffset>",
    };

    // The DbType each value parameter binds with, so that a null is typed: PostgreSQL cannot
    // infer the type of `@p IS NULL` from an untyped parameter. The facet is the DbType of the
    // primitive the parameter binds as, so an int enum gets Int32, a [StoreAsString] enum
    // String and a typed id its underlying type's. An enum that binds by name only because it
    // is compared with an EnumAsString column is switched to String when bound to the maps.
    // A parameter whose [Param] writes a DbType gets none: the written one wins.
    private static EquatableArray<LinqParameterFacet> ReadFacets(
        IReadOnlyList<IParameterSymbol> valueParameters,
        ConventionContext conventions)
    {
        var facets = ImmutableArray.CreateBuilder<LinqParameterFacet>();
        foreach (var parameter in valueParameters)
        {
            if (OrmGenerator.HasWrittenDbType(parameter)) continue;
            var reader = OrmGenerator.BoundReader(parameter.Type, conventions);
            if (reader is null || !FacetReaders.Contains(reader)) continue;
            facets.Add(Facet(parameter.Name, PrimitiveCatalog.GetDbTypeNameFromReader(reader)));
        }
        return new EquatableArray<LinqParameterFacet>(facets.ToImmutable());
    }

    internal static LinqParameterFacet Facet(string parameterName, string dbTypeName)
        => new(parameterName, DbTypePrefix + dbTypeName, (int)(DbType)Enum.Parse(typeof(DbType), dbTypeName));
}
