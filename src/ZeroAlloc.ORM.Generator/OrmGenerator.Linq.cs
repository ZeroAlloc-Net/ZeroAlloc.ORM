using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZeroAlloc.ORM.Generator.Linq;
using ZeroAlloc.ORM.Generator.Model;
using ZeroAlloc.TypeConversions;

namespace ZeroAlloc.ORM.Generator;

// [LinqQuery] wiring. Two incremental steps:
//
//   LinqQueryReads   -- per method, from ForAttributeWithMetadataName. LinqQueryReader.Read
//                       translates the companion's chain, then TransformMethodCore builds the
//                       [Query]-path model. Both are symbol-free, so an edit elsewhere leaves
//                       the step Unchanged.
//   LinqQueryMethods -- the read combined with the entity maps. LinqQueryBinder resolves the
//                       table and columns and renders the SQL into the model. A map edit reruns
//                       only this cheap step.
//
// The SQL is the one input of TransformMethodCore that depends on the maps, and the core needs
// the attribute context, which must not enter a combined step. So the core runs with an empty
// command text and the bound SQL replaces it. That is equivalent because the core reads the text
// only to count statements, and the translator always renders exactly one.
public sealed partial class OrmGenerator
{
    private static IncrementalValuesProvider<QueryMethodWithTypeContext> LinqQueryMethods(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<EquatableArray<EntityMapModel>> entityMaps)
    {
        var bound = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: LinqQueryAttributeFullName,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, ct) => ReadLinqMethod(ctx, ct))
            .WithTrackingName("LinqQueryReads")
            .Combine(entityMaps)
            .Select(static (pair, _) => BindLinqMethod(pair.Left, pair.Right))
            .WithTrackingName("LinqQueryMethods");

        context.RegisterSourceOutput(bound, static (sourceCtx, result) =>
        {
            foreach (var diagnostic in result.Diagnostics)
                ReportStandaloneDiagnostic(sourceCtx, diagnostic);
        });

        return bound
            .Where(static r => r.Method is not null)
            .Select(static (r, _) => r.Method!);
    }

    private static LinqMethodRead ReadLinqMethod(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var translation = LinqQueryReader.Read(ctx, ct);
        if (translation.Statement is null) return new LinqMethodRead(translation, null);

        var core = TransformMethodCore(
            ctx,
            AttributePipelineKind.LinqQuery,
            sql: string.Empty,
            new LinqOverrides(translation.Facets, translation.SourceComment));
        if (core?.Method.Materialization is { } materialization && ReadsSeveralColumns(materialization) is { } member)
        {
            var location = ((MethodDeclarationSyntax)ctx.TargetNode).ReturnType.GetLocation();
            var error = LinqDiagnostics.UnsupportedProjection(
                "'" + member + "' reads several columns; a projected row reads one column per constructor parameter",
                location);
            return new LinqMethodRead(
                LinqTranslation.Failed(translation.SourceComment, new EquatableArray<DiagnosticInfo>(ImmutableArray.Create(error))),
                null);
        }
        return new LinqMethodRead(translation, core);
    }

    // A composite or [Materialize] factory member of the row expands to several reader columns,
    // while the projection gives it one.
    private static string? ReadsSeveralColumns(MaterializationModel materialization)
    {
        foreach (var column in materialization.Columns)
        {
            if (column.InnerColumns.Length > 0) return column.CtorArgName ?? column.TypeName;
        }
        return null;
    }

    private static LinqMethodResult BindLinqMethod(LinqMethodRead read, EquatableArray<EntityMapModel> maps)
    {
        if (read.Translation.Statement is not { } statement || read.Core is not { } core)
            return new LinqMethodResult(null, read.Translation.Diagnostics);

        var bound = LinqQueryBinder.Bind(statement, maps);
        if (bound.Diagnostics.Length > 0) return new LinqMethodResult(null, bound.Diagnostics);

        var method = core.Method with
        {
            Sql = bound.Sql,
            Materialization = ReadEnumsAsStrings(core.Method.Materialization, bound.EnumAsStringOrdinals),
            LinqThrowsOnNoElements = HasNoValueForNoRows(statement),
        };
        method = BindEnumsByName(method, bound.NameBoundEnumParameters, read.Translation.Facets);
        method = UnbindUnreferenced(method, SqlParameterReferences.Collect(statement));
        return new LinqMethodResult(core with { Method = method }, read.Translation.Diagnostics);
    }

    // A scalar Average, Min or Max is NULL over no rows, where LINQ's non-nullable overload
    // throws. Sum is COALESCEd to 0 and Count and Any always have a value.
    private static bool HasNoValueForNoRows(SqlStatement statement)
        => statement is SqlScalarAggregateStatement { Call.Function: not SqlAggregateFunction.Sum };

    // An int-convention enum parameter compared with EnumAsString columns binds the way a
    // [StoreAsString] one does: as its member name, through the EnumAsString convention. The
    // facet the translation derived becomes String; a DbType written on [Param] stays. Either
    // way the parameter's DbTypeCheck now names the reader it binds as, so ZAO015 checks the
    // DbType against a string.
    private static QueryMethodModel BindEnumsByName(
        QueryMethodModel method,
        EquatableArray<string> sqlNames,
        EquatableArray<LinqParameterFacet> generatedFacets)
    {
        if (sqlNames.Length == 0) return method;
        var parameters = method.MethodParameters.Values.ToBuilder();
        var checks = ImmutableArray.CreateBuilder<DbTypeCheck>();
        checks.AddRange(method.DbTypeChecks);
        var stringFacet = LinqQueryReader.Facet(string.Empty, "String");
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            if (parameter.Convention is not { Kind: (int)ConventionKind.Enum } convention
                || !sqlNames.Contains(parameter.ParamNameOverride ?? parameter.Name, System.StringComparer.Ordinal))
            {
                continue;
            }

            // Derived, not written: the translation's facet, or none at all for an enum whose
            // integral type the facet table leaves out, such as a byte enum.
            var derived = parameter.Facets?.DbTypeExpression is null
                || generatedFacets.Any(f => string.Equals(f.ParameterName, parameter.Name, System.StringComparison.Ordinal));
            var facets = !derived
                ? parameter.Facets
                : parameter.Facets is { } current
                    ? current with { DbTypeExpression = stringFacet.DbTypeExpression, DbTypeValue = stringFacet.DbTypeValue }
                    : new ParamFacets(stringFacet.DbTypeExpression, null, null, null, null, stringFacet.DbTypeValue);
            parameters[i] = parameter with
            {
                Convention = convention with { Kind = (int)ConventionKind.EnumAsString, UnderlyingReader = "GetString" },
                Facets = facets,
            };

            // A parameter that had no DbType has no check to update, and String on a string is
            // never rejected, so none is added.
            for (var c = 0; c < checks.Count; c++)
            {
                if (!string.Equals(checks[c].ParameterName, parameter.Name, System.StringComparison.Ordinal)) continue;
                checks[c] = derived
                    ? checks[c] with { BoundReader = "GetString", DbTypeValue = stringFacet.DbTypeValue, DbTypeDisplay = "String" }
                    : checks[c] with { BoundReader = "GetString" };
            }
        }

        return method with
        {
            MethodParameters = new EquatableArray<ParameterInfo>(parameters.MoveToImmutable()),
            DbTypeChecks = new EquatableArray<DbTypeCheck>(checks.ToImmutable()),
        };
    }

    // A value parameter the rendered SQL never names, such as one only a dropped projection
    // used, would cost a DbParameter per call for nothing. It stays in the signature.
    private static QueryMethodModel UnbindUnreferenced(QueryMethodModel method, System.Collections.Generic.HashSet<string> referenced)
    {
        var parameters = method.MethodParameters.Values.ToBuilder();
        var changed = false;
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            if (parameter.IsCancellationToken || parameter.IsTransaction
                || referenced.Contains(parameter.ParamNameOverride ?? parameter.Name))
            {
                continue;
            }
            parameters[i] = parameter with { IsUnreferenced = true };
            changed = true;
        }
        return changed
            ? method with { MethodParameters = new EquatableArray<ParameterInfo>(parameters.MoveToImmutable()) }
            : method;
    }

    // A column mapped with EnumAsString stores the member name. When the row member it feeds is
    // an int-convention enum, read it the way a [StoreAsString] enum is read: GetString, then
    // Enum.Parse. Row members of any other type keep their own reader.
    private static MaterializationModel? ReadEnumsAsStrings(MaterializationModel? materialization, EquatableArray<int> ordinals)
    {
        if (materialization is null || ordinals.Length == 0) return materialization;
        var columns = materialization.Columns.Values.ToBuilder();
        foreach (var ordinal in ordinals)
        {
            if (ordinal >= columns.Count) continue;
            var column = columns[ordinal];
            if (column.Convention is not { Kind: (int)ConventionKind.Enum } convention) continue;
            columns[ordinal] = column with
            {
                GetterMethod = "GetString",
                Convention = convention with { Kind = (int)ConventionKind.EnumAsString, UnderlyingReader = "GetString" },
            };
        }
        return materialization with { Columns = new EquatableArray<ColumnBinding>(columns.MoveToImmutable()) };
    }
}
