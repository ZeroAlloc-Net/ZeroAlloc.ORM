using System.Collections.Immutable;
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
        };
        return new LinqMethodResult(core with { Method = method }, read.Translation.Diagnostics);
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
