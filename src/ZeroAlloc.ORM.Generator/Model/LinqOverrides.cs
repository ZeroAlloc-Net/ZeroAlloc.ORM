namespace ZeroAlloc.ORM.Generator.Model;

// [LinqQuery] — what the LINQ translation adds on top of the [Query] path. Passed
// to TransformMethodCore next to the generated SQL; null for every attribute
// pipeline that reads its SQL from the attribute.
//
//   Facets        -- the DbType each companion parameter binds with, derived from
//                    its CLR type so that, for one, a null value is still typed.
//   SourceComment -- one line describing the LINQ chain; becomes
//                    QueryMethodModel.LinqSourceComment.
//
// Plain strings, ints and EquatableArray only, so the model stays cache-safe.
internal sealed record LinqOverrides(
    EquatableArray<LinqParameterFacet> Facets,
    string SourceComment);

// One parameter's DbType facet from the LINQ translation. Applied to the matching
// ParameterInfo.Facets the way `[Param(DbType = ...)]` would be, so the emit sets
// the DbType and ZAO015 checks it against the dialect through a DbTypeCheck.
//
//   ParameterName    -- the C# parameter name on the [LinqQuery] method.
//   DbTypeExpression -- C# expression, such as `global::System.Data.DbType.Int32`.
//   DbTypeValue      -- the numeric value of DbTypeExpression, for ZAO015.
internal sealed record LinqParameterFacet(
    string ParameterName,
    string DbTypeExpression,
    int DbTypeValue);
