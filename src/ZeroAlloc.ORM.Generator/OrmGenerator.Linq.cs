using System.Collections.Generic;
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
// command text and the bound SQL replaces it. The core reads the text only to count statements:
// for ZAO008, which needs a multi-result return type for several statements; for ZAO032/033,
// which it skips for [LinqQuery], since the translator renders one statement per tuple element;
// and for the batch strategy, which the bind recomputes from the bound SQL.
//
// [LinqCommand] is wired the same way, as LinqCommandReads and LinqCommandMethods. Its core runs
// as [Command] with Kind NonQuery, the attribute declaring no Kind, so the method takes the
// [Command] non-query emit; ThrowOnNoRows adds the zero-rows check to it.
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
                transform: static (ctx, ct) => ReadLinqMethod(ctx, isCommand: false, ct))
            .WithTrackingName("LinqQueryReads")
            .Combine(entityMaps)
            .Select(static (pair, _) => BindLinqMethod(pair.Left, pair.Right))
            .WithTrackingName("LinqQueryMethods");
        return ReportAndKeepBound(context, bound, LinqDiagnostics.LinqQuery);
    }

    private static IncrementalValuesProvider<QueryMethodWithTypeContext> LinqCommandMethods(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<EquatableArray<EntityMapModel>> entityMaps)
    {
        var bound = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: LinqCommandAttributeFullName,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, ct) => ReadLinqMethod(ctx, isCommand: true, ct))
            .WithTrackingName("LinqCommandReads")
            .Combine(entityMaps)
            .Select(static (pair, _) => BindLinqMethod(pair.Left, pair.Right))
            .WithTrackingName("LinqCommandMethods");
        return ReportAndKeepBound(context, bound, LinqDiagnostics.LinqCommand);
    }

    // Reports each method's LINQ diagnostics, with the attribute their messages name, and keeps
    // the methods that bound.
    private static IncrementalValuesProvider<QueryMethodWithTypeContext> ReportAndKeepBound(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<LinqMethodResult> bound,
        string attribute)
    {
        context.RegisterSourceOutput(bound, (sourceCtx, result) =>
        {
            foreach (var diagnostic in result.Diagnostics)
                ReportStandaloneDiagnostic(sourceCtx, LinqDiagnostics.ForAttribute(diagnostic, attribute));
        });

        return bound
            .Where(static r => r.Method is not null)
            .Select(static (r, _) => r.Method!);
    }

    private static LinqMethodRead ReadLinqMethod(GeneratorAttributeSyntaxContext ctx, bool isCommand, CancellationToken ct)
    {
        var translation = LinqQueryReader.Read(ctx, isCommand, ct);
        if (!translation.Succeeded) return new LinqMethodRead(translation, null);

        var core = TransformMethodCore(
            ctx,
            isCommand ? AttributePipelineKind.Command : AttributePipelineKind.LinqQuery,
            sql: string.Empty,
            new LinqOverrides(translation.Facets, translation.SourceComment)
            {
                NoRowsMessage = isCommand && ThrowsOnNoRows(ctx) ? NoRowsMessage(ctx) : null,
                JsonParameters = translation.JsonParameters,
            });
        if (core is not null && ReadsSeveralColumns(core.Method, translation) is { } member)
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

    // [LinqCommand(ThrowOnNoRows = true)].
    private static bool ThrowsOnNoRows(GeneratorAttributeSyntaxContext ctx)
    {
        foreach (var named in ctx.Attributes[0].NamedArguments)
        {
            if (string.Equals(named.Key, "ThrowOnNoRows", System.StringComparison.Ordinal) && named.Value.Value is true)
                return true;
        }
        return false;
    }

    // The ZeroAllocOrmConcurrencyException message, which names the type and the method.
    private static string NoRowsMessage(GeneratorAttributeSyntaxContext ctx)
        => "'" + ctx.TargetSymbol.ContainingType.Name + "." + ctx.TargetSymbol.Name
            + "' affected no rows; the row was changed or removed since it was read";

    // A composite or [Materialize] factory member of the row expands to several reader columns,
    // while the projection gives it one. Every element of a tuple is checked, element i against
    // statement i. A parameter fed a row member whose type is an object is left to the binder,
    // where the map decides at the member: an owned object is ZAO107, a composite column ZAO102.
    private static string? ReadsSeveralColumns(QueryMethodModel method, LinqTranslation translation)
    {
        if (method.Materialization is { } materialization
            && ReadsSeveralColumns(materialization.Columns, WholeObjectItems(translation, 0)) is { } member)
        {
            return member;
        }
        if (method.MultiResultMaterialization is { } multi)
        {
            for (var i = 0; i < multi.Elements.Length; i++)
            {
                if (ReadsSeveralColumns(multi.Elements[i].Columns, WholeObjectItems(translation, i)) is { } elementMember)
                    return elementMember;
            }
        }
        return null;
    }

    private static string? ReadsSeveralColumns(EquatableArray<ColumnBinding> columns, HashSet<int> skipped)
    {
        for (var i = 0; i < columns.Length; i++)
        {
            if (columns[i].InnerColumns.Length > 0 && !skipped.Contains(i))
                return columns[i].CtorArgName ?? columns[i].TypeName;
        }
        return null;
    }

    // The SELECT positions of statement `index` that read a row member whose type is an object.
    private static HashSet<int> WholeObjectItems(LinqTranslation translation, int index)
    {
        var positions = new HashSet<int>();
        if (index < translation.Statements.Length && translation.Statements[index] is SqlSelectStatement select)
        {
            for (var i = 0; i < select.Projection.Length; i++)
            {
                if (select.Projection[i] is SqlMemberExpression { WholeObject: not null }) positions.Add(i);
            }
        }
        return positions;
    }

    private static LinqMethodResult BindLinqMethod(LinqMethodRead read, EquatableArray<EntityMapModel> maps)
    {
        if (!read.Translation.Succeeded || read.Core is not { } core)
            return new LinqMethodResult(null, read.Translation.Diagnostics);

        var statements = read.Translation.Statements;
        var bound = LinqQueryBinder.Bind(statements, maps, read.Translation.LiftedProjections);
        if (bound.Diagnostics.Length > 0) return new LinqMethodResult(null, bound.Diagnostics);

        var method = core.Method with
        {
            Sql = bound.Sql,
            // BatchMode.Auto: one statement runs as a single command, several as one batch
            // with the ;-joined fallback, as for [Query].
            Strategy = ResolveBatchStrategy(bound.Sql, batchMode: 0),
        };
        method = statements.Length == 1
            ? method with
            {
                Materialization = ReadEnumsAsStrings(method.Materialization, bound.EnumAsStringOrdinals[0]),
                LinqThrowsOnNoElements = HasNoValueForNoRows(statements[0]),
            }
            : method with
            {
                MultiResultMaterialization = BindElements(method.MultiResultMaterialization, statements, bound.EnumAsStringOrdinals),
                LinqStatementParameters = StatementParameters(statements),
                LinqStatements = bound.Statements,
            };
        method = BindEnumsByName(method, bound.NameBoundEnumParameters, read.Translation.Facets);
        method = UnbindUnreferenced(method, ReferencedParameters(statements));
        return new LinqMethodResult(core with { Method = method }, read.Translation.Diagnostics);
    }

    // Each tuple element reads its own statement's columns: enums stored by name, a missing row
    // as null, and no value from an Average, Min or Max over no rows as LINQ's exception.
    private static MultiResultMaterializationModel? BindElements(
        MultiResultMaterializationModel? materialization,
        EquatableArray<SqlStatement> statements,
        EquatableArray<EquatableArray<int>> ordinals)
    {
        if (materialization is null) return null;
        var elements = materialization.Elements.Values.ToBuilder();
        for (var i = 0; i < elements.Count && i < statements.Length; i++)
        {
            var element = elements[i];
            elements[i] = element with
            {
                Columns = ReadEnumsAsStrings(element.Columns, ordinals[i]),
                NullWhenNoRow = element.Kind == MultiResultElementKind.Row,
                ThrowsOnNoElements = element.Kind == MultiResultElementKind.Scalar && HasNoValueForNoRows(statements[i]),
            };
        }
        return materialization with { Elements = new EquatableArray<MultiResultElement>(elements.MoveToImmutable()) };
    }

    // The SQL names each statement renders, so each batch command binds only its own.
    private static EquatableArray<EquatableArray<string>> StatementParameters(EquatableArray<SqlStatement> statements)
    {
        var perStatement = ImmutableArray.CreateBuilder<EquatableArray<string>>(statements.Length);
        foreach (var statement in statements)
        {
            var names = SqlParameterReferences.Collect(statement).ToImmutableArray().Sort(System.StringComparer.Ordinal);
            perStatement.Add(new EquatableArray<string>(names));
        }
        return new EquatableArray<EquatableArray<string>>(perStatement.MoveToImmutable());
    }

    // Every SQL name any statement renders.
    private static System.Collections.Generic.HashSet<string> ReferencedParameters(EquatableArray<SqlStatement> statements)
    {
        var referenced = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var statement in statements)
            referenced.UnionWith(SqlParameterReferences.Collect(statement));
        return referenced;
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
        return materialization with { Columns = ReadEnumsAsStrings(materialization.Columns, ordinals) };
    }

    private static EquatableArray<ColumnBinding> ReadEnumsAsStrings(EquatableArray<ColumnBinding> bindings, EquatableArray<int> ordinals)
    {
        if (ordinals.Length == 0) return bindings;
        var columns = bindings.Values.ToBuilder();
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
        return new EquatableArray<ColumnBinding>(columns.MoveToImmutable());
    }
}
