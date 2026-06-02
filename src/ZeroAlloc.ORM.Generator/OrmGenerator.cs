using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ZeroAlloc.ORM.Generator.Diagnostics;
using ZeroAlloc.ORM.Generator.Model;
using ZeroAlloc.TypeConversions;

namespace ZeroAlloc.ORM.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class OrmGenerator : IIncrementalGenerator
{
    private const string QueryAttributeFullName = "ZeroAlloc.ORM.QueryAttribute";
    private const string CommandAttributeFullName = "ZeroAlloc.ORM.CommandAttribute";
    private const string StoredProcedureAttributeFullName = "ZeroAlloc.ORM.StoredProcedureAttribute";
    private const string IAsyncDbConnectionFullName = "System.Data.Async.IAsyncDbConnection";
    private const string IAsyncDbConnectionSimpleName = "IAsyncDbConnection";
    private const string GeneratorVersion = "0.1.0";
    private const string GeneratedCodeAttribute =
        "[global::System.CodeDom.Compiler.GeneratedCode(\"ZeroAlloc.ORM.Generator\", \"" + GeneratorVersion + "\")]";

    // v0.5 Phase A (post-review Fix 15) — fully-qualified display format with
    // nullable reference-type annotations preserved. Used at every site that
    // computes a ToDisplayString on a parameter type for FlatRow / DomainEntity /
    // composite materialization. Hoisted to a static readonly so the
    // SymbolDisplayFormat builder isn't rebuilt per Resolve() / per ctor param.
    private static readonly SymbolDisplayFormat TypeDisplayFormat =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var queryMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: QueryAttributeFullName,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => TransformMethod(ctx, AttributePipelineKind.Query))
            .Where(static m => m is not null)!;

        // v0.4 Phase A — [Command] attribute pickup. Re-uses the same TransformMethod
        // pathway (parameter binding, connection-access resolution, ZAO* diagnostics)
        // and sets IsCommand = true so emit dispatch can pick the CommandNonQuery
        // shape. Both pipelines feed into the same grouping step; methods carrying
        // both [Query] and [Command] surface ZAO005 (extended in v0.4 from the v0.3
        // single-Query rule).
        //
        // Incremental-cache trade-off: the union below collapses all pipelines via
        // Collect() + SelectMany, which means ANY source edit re-runs the whole union
        // + grouping step rather than re-running only the pipeline that saw the
        // change. We accept that cost in v0.4 because (a) ForAttributeWithMetadataName
        // is still the high-performance entry point for the per-method transforms and
        // (b) the union output is small relative to the per-method symbol work that
        // it caches. The backlog item "v0.4-CLN — single-pipeline architecture for
        // [Query]+[Command]+[StoredProcedure]" tracks the future investigation of
        // folding all three into one ForAttributeWithMetadataName call to preserve
        // per-method incremental-cache granularity.
        var commandMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: CommandAttributeFullName,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => TransformMethod(ctx, AttributePipelineKind.Command))
            .Where(static m => m is not null)!;

        // v0.4 Phase D — [StoredProcedure] attribute pickup. Third pipeline; identical
        // structural pattern to [Command]. Sets IsStoredProcedure = true so emit
        // dispatch can swap CommandText for ProcedureName + set CommandType =
        // StoredProcedure. ZAO005 surfaces a method that carries any pair of
        // [Query] / [Command] / [StoredProcedure] (or all three) via the
        // ormAttrCount > 1 check in TransformMethod, with the union dedup below
        // dropping duplicate entries so the diagnostic fires exactly once per method.
        var storedProcedureMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: StoredProcedureAttributeFullName,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => TransformMethod(ctx, AttributePipelineKind.StoredProcedure))
            .Where(static m => m is not null)!;

        // Group by containing-type FQN. Every QueryMethodWithTypeContext within a
        // group carries the same type-scoped fields (ConnectionAccess, partial-ness,
        // location, etc.) — we take them from g.First() instead of duplicating them
        // on each QueryMethodModel. R8 hoist: this removes the prior fallback that
        // grabbed type-properties off `repo.Methods.Values[0]` downstream.
        //
        // v0.4 Phase D: [Query] + [Command] + [StoredProcedure] pipelines are unioned
        // before grouping so a single QueryRepositoryModel covers all three attribute
        // kinds on the same type. The (containingTypeFullName, methodName) tuple
        // uniquely identifies a method; we dedupe on that key inside the union so a
        // method that picks up multiple pipelines (the ZAO005 multi-attribute case)
        // doesn't produce two/three emit slots. Ordering matters for the dedup
        // tie-break: pair.Left ([Query]) wins over [Command], which wins over
        // [StoredProcedure] — but since ZAO005 fires from EVERY pipeline that
        // transforms the method, the diagnostic surfaces regardless of which entry
        // the dedup keeps, while emit gets a single deterministic slot.
        var allMethods = queryMethods.Collect()
            .Combine(commandMethods.Collect())
            .Combine(storedProcedureMethods.Collect())
            .SelectMany(static (combined, _) =>
            {
                var pair = combined.Left;
                var sprocs = combined.Right;
                var seen = new HashSet<(string, string)>();
                var union = ImmutableArray.CreateBuilder<QueryMethodWithTypeContext>();
                foreach (var m in pair.Left)
                {
                    if (m is null) continue;
                    var key = (m.Method.ContainingTypeFullName, m.Method.MethodName);
                    if (seen.Add(key)) union.Add(m);
                }
                foreach (var m in pair.Right)
                {
                    if (m is null) continue;
                    var key = (m.Method.ContainingTypeFullName, m.Method.MethodName);
                    if (seen.Add(key)) union.Add(m);
                }
                foreach (var m in sprocs)
                {
                    if (m is null) continue;
                    var key = (m.Method.ContainingTypeFullName, m.Method.MethodName);
                    if (seen.Add(key)) union.Add(m);
                }
                return union.ToImmutable();
            });

        var grouped = allMethods.Collect()
            .SelectMany(static (methods, _) =>
                methods.GroupBy(m => m!.Method.ContainingTypeFullName, StringComparer.Ordinal)
                       .Select(g =>
                       {
                           var first = g.First()!;
                           return new QueryRepositoryModel(
                               ContainingTypeFullName: g.Key,
                               ContainingTypeName: first.ContainingTypeName,
                               Namespace: first.Namespace,
                               ConnectionAccess: first.ConnectionAccess,
                               ConnectionResolved: first.ConnectionResolved,
                               ContainingTypePartial: first.ContainingTypePartial,
                               ContainingTypeLocation: first.ContainingTypeLocation,
                               Methods: new EquatableArray<QueryMethodModel>(g.Select(x => x!.Method).ToImmutableArray()));
                       }));

        context.RegisterSourceOutput(grouped, (sourceCtx, repo) =>
        {
            var hadError = ReportDiagnostics(sourceCtx, repo);
            if (!hadError) EmitRepository(sourceCtx, repo);
        });

        // ZAO042 — [StoreAsString] is only legal on enum types. This lives in its own
        // pipeline (not in TransformMethod) because the attribute is type-scoped, not
        // method-scoped: we want to fire even if no [Query] method references the
        // mis-annotated type. ForAttributeWithMetadataName visits every type carrying
        // the attribute, regardless of usage.
        var storeAsStringDiagnostics = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: "ZeroAlloc.ORM.StoreAsStringAttribute",
                predicate: static (node, _) => node is BaseTypeDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is INamedTypeSymbol typeSymbol
                        && typeSymbol.TypeKind != TypeKind.Enum)
                    {
                        return new DiagnosticInfo(
                            DescriptorId: "ZAO042",
                            Location: LocationInfo.From(ctx.TargetNode.GetLocation()),
                            MessageArgs: new EquatableArray<string>(
                                ImmutableArray.Create(typeSymbol.ToDisplayString())));
                    }
                    return null;
                })
            .Where(static d => d is not null);

        context.RegisterSourceOutput(storeAsStringDiagnostics, (sourceCtx, diag) =>
        {
            if (diag is null) return;
            var descriptor = LookupDescriptor(diag.DescriptorId);
            if (descriptor is null) return;
            object[] args = diag.MessageArgs.Values.IsDefault
                ? Array.Empty<object>()
                : diag.MessageArgs.Values.ToArray();
            sourceCtx.ReportDiagnostic(Diagnostic.Create(
                descriptor,
                diag.Location?.ToLocation(),
                args));
        });
    }

    private static QueryMethodWithTypeContext? TransformMethod(GeneratorAttributeSyntaxContext ctx, AttributePipelineKind pipelineKind)
    {
        // Convenience bools — every downstream branch reads these instead of
        // re-pattern-matching the enum. The original Phase A code carried
        // `isCommandAttribute`; Phase D adds `isStoredProcedureAttribute` and
        // keeps the existing variable name for diff minimization.
        var isCommandAttribute = pipelineKind == AttributePipelineKind.Command;
        var isStoredProcedureAttribute = pipelineKind == AttributePipelineKind.StoredProcedure;
        if (ctx.TargetSymbol is not IMethodSymbol method) return null;
        if (method.ContainingType is not INamedTypeSymbol containing) return null;
        if (ctx.TargetNode is not MethodDeclarationSyntax methodSyntax) return null;

        // ConventionDiscovery needs a Compilation for well-known attribute lookups. We
        // construct the context once per method and re-use it across return-type and
        // per-parameter classification so the lookup cost amortizes.
        var conventionContext = new ConventionContext(ctx.SemanticModel.Compilation);

        // The triggering attribute. For [Query] this is the QueryAttribute; for the
        // [Command] pipeline this is the CommandAttribute; for [StoredProcedure]
        // (Phase D) it's StoredProcedureAttribute. All three share a single
        // string-typed ctor arg — Query/Command call it "Sql", StoredProcedure calls
        // it "ProcedureName" — so we pull the raw value into `firstStringArg` and
        // then dispatch to the right model field. For sprocs, m.Sql stays empty
        // (multi-result-set classification doesn't read it for the sproc path,
        // and ZAO008/ZAO032/ZAO033 SQL-statement-counting checks are suppressed
        // for sprocs since the procedure body lives server-side).
        var triggeringAttribute = ctx.Attributes.FirstOrDefault();
        // All three triggering attributes today declare a `string` first ctor arg
        // ([Query(Sql)], [Command(Sql)], [StoredProcedure(ProcedureName)]). The branch
        // where Value isn't a string is structurally unreachable as long as the
        // Abstractions surface stays string-typed. The defensive `is string` pattern
        // (replacing the older `as string ?? string.Empty`) ensures that if a future
        // overload ever changed the ctor signature, the failure is loud (the empty
        // fallback would silently emit an empty CommandText, which on the sproc
        // pipeline becomes a confusing runtime "stored procedure '' not found" error).
        var rawCtorArg = triggeringAttribute?.ConstructorArguments.FirstOrDefault().Value;
        var firstStringArg = rawCtorArg is string s
            ? s
            : rawCtorArg is null
                ? string.Empty
                : throw new global::System.InvalidOperationException(
                    $"ORM triggering attribute ctor arg expected to be string, was {rawCtorArg.GetType().Name}.");
        var sql = isStoredProcedureAttribute ? string.Empty : firstStringArg;
        var procedureName = isStoredProcedureAttribute ? firstStringArg : string.Empty;

        // v0.4 Phase A — read [Command(Kind = ...)] from the named args. Default is
        // NonQuery to mirror the abstraction default. Only consulted when this method
        // came in via the [Command] pipeline; [Query] sets CommandKind to NonQuery
        // (irrelevant — IsCommand stays false).
        var commandKind = CommandKindModel.NonQuery;
        if (isCommandAttribute && triggeringAttribute is not null)
        {
            foreach (var named in triggeringAttribute.NamedArguments)
            {
                if (string.Equals(named.Key, "Kind", StringComparison.Ordinal)
                    && named.Value.Value is int kindValue)
                {
                    commandKind = (CommandKindModel)kindValue;
                }
            }
        }

        var (connectionAccess, connectionResolved) = ResolveConnectionAccess(containing);

        // Containing type's first-declaration location, for type-scoped diagnostics (ZAO003/ZAO004).
        // Also detect whether ANY declaration carries the `partial` modifier.
        LocationInfo? containingTypeLocation = null;
        var containingTypePartial = false;
        foreach (var syntaxRef in containing.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is TypeDeclarationSyntax td)
            {
                containingTypeLocation ??= LocationInfo.From(td.Identifier.GetLocation());
                if (td.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                    containingTypePartial = true;
            }
        }

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        // ZAO061 — [StoredProcedure("")] / [StoredProcedure(null)] / whitespace-only.
        // Originally scheduled for Phase F; brought forward in the Phase D fix-up so
        // adopters never hit the silent-empty-CommandText runtime failure (which
        // surfaces as a provider-specific "could not find stored procedure ''"
        // message and is materially harder to diagnose than a compile-time error).
        // Error severity so the existing hadError gate suppresses emit.
        if (isStoredProcedureAttribute && string.IsNullOrWhiteSpace(procedureName))
        {
            diagnostics.Add(new DiagnosticInfo(
                DescriptorId: "ZAO061",
                Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(method.Name))));
        }

        // ZAO001 — method must be partial.
        if (!methodSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
        {
            diagnostics.Add(new DiagnosticInfo(
                DescriptorId: "ZAO001",
                Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(method.Name))));
        }

        // ZAO005 — multiple ORM attributes on one method.
        // v0.4 Phase D extends from the v0.4-A two-attribute check to a three-way
        // exclusivity rule: counts QueryAttribute + CommandAttribute +
        // StoredProcedureAttribute. Counting all three pipelines' attributes on the
        // same method covers every pair (Query+Command, Query+StoredProcedure,
        // Command+StoredProcedure) and the all-three case via a single >1 threshold.
        //
        // Every pipeline emits ZAO005 from TransformMethod (since method.GetAttributes()
        // surfaces ALL attributes regardless of which pipeline triggered the transform).
        // The duplicates are dropped at the union step (above) where the deduper keeps
        // the first entry per (containing-type, method-name) — pair.Left.Left ([Query])
        // wins the tie, then [Command], then [StoredProcedure].
        var ormAttrCount = method.GetAttributes()
            .Count(a =>
            {
                var fqn = a.AttributeClass?.ToDisplayString();
                return string.Equals(fqn, "ZeroAlloc.ORM.QueryAttribute", StringComparison.Ordinal)
                    || string.Equals(fqn, "ZeroAlloc.ORM.CommandAttribute", StringComparison.Ordinal)
                    || string.Equals(fqn, "ZeroAlloc.ORM.StoredProcedureAttribute", StringComparison.Ordinal);
            });
        if (ormAttrCount > 1)
        {
            diagnostics.Add(new DiagnosticInfo(
                DescriptorId: "ZAO005",
                Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(method.Name))));
        }

        // v1.3 — ZAO074: CommandKind.BulkInsert is meaningful only on [Command].
        // The Kind named-arg lives exclusively on CommandAttribute (QueryAttribute /
        // StoredProcedureAttribute don't declare it), so the only way this
        // diagnostic fires is when a method carries BOTH [Command(Kind = BulkInsert)]
        // AND [Query] / [StoredProcedure] — ZAO005 also fires in that case. ZAO074
        // is Info severity because it pinpoints WHICH attribute the adopter should
        // change to honour the Kind. We surface it from the [Query] / [StoredProcedure]
        // pipeline (the "non-Command" branch) so the squiggle appears on the offending
        // companion attribute, not the [Command] one. Pipeline self-fires only — we
        // skip when this is the [Command] pipeline because that's the legitimate path.
        if (!isCommandAttribute)
        {
            foreach (var attr in method.GetAttributes())
            {
                if (!string.Equals(
                        attr.AttributeClass?.ToDisplayString(),
                        CommandAttributeFullName,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (var named in attr.NamedArguments)
                {
                    if (string.Equals(named.Key, "Kind", StringComparison.Ordinal)
                        && named.Value.Value is int companionKindValue
                        && (CommandKindModel)companionKindValue == CommandKindModel.BulkInsert)
                    {
                        var companionAttributeLabel = isStoredProcedureAttribute
                            ? "[StoredProcedure]"
                            : "[Query]";
                        diagnostics.Add(new DiagnosticInfo(
                            DescriptorId: "ZAO074",
                            Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                            MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                                method.Name,
                                companionAttributeLabel))));
                    }
                }
                break;
            }
        }

        // ZAO009 — partial declaration must not carry the `async` keyword (warning).
        if (methodSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword)))
        {
            diagnostics.Add(new DiagnosticInfo(
                DescriptorId: "ZAO009",
                Location: LocationInfo.From(methodSyntax.Modifiers.First(m => m.IsKind(SyntaxKind.AsyncKeyword)).GetLocation()),
                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(method.Name))));
        }

        // ZAO008 — multi-statement SQL with a single-result return type.
        // v0.4 Phase C — exempt [Command] methods. Multi-statement SQL is
        // legitimate for Identity (e.g. `INSERT ...; SELECT SCOPE_IDENTITY()`
        // on SQL Server, `INSERT ...; SELECT last_insert_rowid()` on Sqlite)
        // and harmless for Scalar / NonQuery — the underlying ADO.NET driver
        // executes the joined statements as a unit and ExecuteScalarAsync /
        // ExecuteNonQueryAsync just consume the relevant result. ZAO008 only
        // applies to [Query] methods where a `;` paired with a single-row /
        // single-scalar return type means the second statement is silently
        // discarded — surface as an error there but not on commands.
        // v0.4 Phase D fix-up — explicit !isStoredProcedureAttribute suppression
        // matches the declarative pattern used by ZAO032/ZAO033 below. Sprocs already
        // pass through silently because m.Sql is empty for the sproc pipeline and
        // CountStatements("") returns 0, so the && chain short-circuits. Adding the
        // explicit gate makes the intent visible at the call site without changing
        // behaviour (the boolean expression evaluates identically either way).
        if (!isCommandAttribute
            && !isStoredProcedureAttribute
            && SqlStatementSplitter.CountStatements(sql) > 1
            && !IsMultiResultReturnType(method.ReturnType))
        {
            diagnostics.Add(new DiagnosticInfo(
                DescriptorId: "ZAO008",
                Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(method.Name))));
        }

        // ZAO007 — IAsyncEnumerable<T> requires a CT with [EnumeratorCancellation].
        // Two distinct cases share the diagnostic id; the second message arg disambiguates
        // them so the adopter sees a fix that actually applies to their code:
        //   * a CT param exists but is missing the attribute — they need to add it
        //   * no CT param at all — they need to add the param itself
        if (IsIAsyncEnumerable(method.ReturnType))
        {
            var ctParam = method.Parameters.FirstOrDefault(p =>
                string.Equals(p.Type.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal));
            var hasEnumeratorCancellation = ctParam is not null && ctParam.GetAttributes().Any(a =>
                string.Equals(a.AttributeClass?.ToDisplayString(),
                    "System.Runtime.CompilerServices.EnumeratorCancellationAttribute",
                    StringComparison.Ordinal));
            if (!hasEnumeratorCancellation)
            {
                var reason = ctParam is null
                    ? "has no CancellationToken parameter"
                    : "its CancellationToken parameter lacks [EnumeratorCancellation]";
                diagnostics.Add(new DiagnosticInfo(
                    DescriptorId: "ZAO007",
                    Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                    MessageArgs: new EquatableArray<string>(ImmutableArray.Create(method.Name, reason))));
            }
        }

        // ZAO006 — at most one CancellationToken parameter (warning).
        var ctParamCount = method.Parameters.Count(p =>
            string.Equals(p.Type.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal));
        if (ctParamCount > 1)
        {
            diagnostics.Add(new DiagnosticInfo(
                DescriptorId: "ZAO006",
                Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(method.Name))));
        }

        // ZAO002 — return type must be Task[<T>], ValueTask[<T>], or IAsyncEnumerable<T>.
        if (!IsSupportedReturnType(method.ReturnType))
        {
            diagnostics.Add(new DiagnosticInfo(
                DescriptorId: "ZAO002",
                Location: LocationInfo.From(methodSyntax.ReturnType.GetLocation()),
                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                    method.Name,
                    method.ReturnType.ToDisplayString()))));
        }

        // ZAO020 — informational note for [Query(FromResource = true)] which the
        // generator accepts but does not yet honour at emit time. The value flows
        // through but is silently ignored by codegen; the info diagnostic keeps
        // adopters from believing they're getting behavior that isn't there.
        // Fires only when the attribute author explicitly set FromResource=true.
        //
        // Note: ZAO021 (BatchMode non-Auto deferred) was retired in v0.3 Phase B.5
        // since BatchMode.Always / BatchMode.Never are now honoured by the
        // MultiResultSet emit. See AnalyzerReleases.Unshipped.md.
        //
        // The Batch named-argument is also read once here and fed into
        // ResolveBatchStrategy below — keeping the read in one place avoids a
        // second scan over the attribute's NamedArguments.
        var batchMode = 0; // BatchMode.Auto default
        var batchExplicitlySet = false;
        if (triggeringAttribute is not null)
        {
            foreach (var named in triggeringAttribute.NamedArguments)
            {
                if (string.Equals(named.Key, "FromResource", StringComparison.Ordinal)
                    && named.Value.Value is bool fromResource
                    && fromResource)
                {
                    // ZAO020 fires for [Query], [Command], and [StoredProcedure] from
                    // v0.4 onwards; pass the triggering attribute name as the second
                    // arg so the message reflects what the adopter actually wrote.
                    // The StoredProcedure branch is unreachable today (the sproc
                    // attribute has no FromResource named arg), but the three-way
                    // switch keeps the mapping robust against future surface changes.
                    var attributeName = pipelineKind switch
                    {
                        AttributePipelineKind.Command => "Command",
                        AttributePipelineKind.StoredProcedure => "StoredProcedure",
                        _ => "Query",
                    };
                    diagnostics.Add(new DiagnosticInfo(
                        DescriptorId: "ZAO020",
                        Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                        MessageArgs: new EquatableArray<string>(ImmutableArray.Create(method.Name, attributeName))));
                }
                else if (string.Equals(named.Key, "Batch", StringComparison.Ordinal)
                    && named.Value.Value is int batchValue)
                {
                    batchMode = batchValue;
                    batchExplicitlySet = true;
                }
            }
        }

        // v1.0 Phase C (v0.4-CLN5) — ZAO064: `[StoredProcedure(Batch=...)]` is
        // accepted only for symmetry with `[Query]` / `[Command]` but has no
        // effect on the sproc pipeline (the procedure call is always a single
        // DbCommand whose result-set traversal lives in the materializer). Fire
        // ZAO064 only when the adopter explicitly wrote a non-default value;
        // omitting the named arg (sproc default == BatchMode.Never == 2)
        // must NOT fire. Info severity — the shape still emits.
        if (isStoredProcedureAttribute && batchExplicitlySet && batchMode != 2)
        {
            var batchModeName = batchMode switch
            {
                0 => "Auto",
                1 => "Always",
                2 => "Never",
                _ => batchMode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            diagnostics.Add(new DiagnosticInfo(
                DescriptorId: "ZAO064",
                Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                    method.Name,
                    "BatchMode." + batchModeName))));
        }

        var strategy = ResolveBatchStrategy(sql, batchMode);

        var (shape, nullableReaderMethod, materialization, multiResultMaterialization, hasReturnValue, sprocOutputParamsMaterialization, bulkInsertMaterialization) = ClassifyEmitShape(
            method,
            conventionContext,
            isCommandAttribute,
            commandKind,
            isStoredProcedureAttribute,
            diagnostics,
            LocationInfo.From(methodSyntax.ReturnType.GetLocation()),
            sql);

        // v0.5 Phase C.2 — ZAO050: warn on every nullable composite position.
        // Three triggers:
        //
        //   * Scalar return Task<Money?> — Materialization.Kind == Composite AND
        //     Materialization.IsNullable.
        //   * Nested nullable composite (`record OrderRow(int Id, Money? Total)`)
        //     — any ColumnBinding with InnerColumns.Length > 0 AND IsNullable.
        //
        // The diagnostic location is the return-type syntax (the user's
        // declaration of the nullable composite type). Diagnostic message
        // names the method and the composite type so adopters can suppress
        // narrowly. Warning severity — composite all-or-nothing is a
        // supported shape, the warning just flags the runtime contract.
        if (materialization is not null)
        {
            if (materialization.Kind == MaterializationKind.Composite && materialization.IsNullable)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DescriptorId: "ZAO050",
                    Location: LocationInfo.From(methodSyntax.ReturnType.GetLocation()),
                    MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                        method.Name,
                        materialization.TargetTypeFullName,
                        "return position"))));
            }
            else
            {
                foreach (var col in materialization.Columns)
                {
                    if (col.InnerColumns.Length > 0 && col.IsNullable)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DescriptorId: "ZAO050",
                            Location: LocationInfo.From(methodSyntax.ReturnType.GetLocation()),
                            MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                                method.Name,
                                col.TypeName,
                                "nested in return type"))));
                    }
                }
            }
        }

        // ZAO002 — [Command(Kind = Scalar | Identity)] requires a value-returning
        // shape. Scalar accepts any primitive / VO / enum (including the nullable
        // Task<T?> variant); Identity is narrower (non-nullable int / long / Guid,
        // optionally wrapped in a value-object). When the return type doesn't
        // classify, raise the compile-time diagnostic here so the adopter sees the
        // failure at build time instead of hitting the runtime stub. The existing
        // ZAO002 path at the top of TransformMethod covers the "return type isn't
        // even Task<T>" case via IsSupportedReturnType; THIS branch covers the
        // more subtle "Task<T> but T isn't a supported shape on this Kind" case.
        if (shape == EmitShape.Unknown
            && isCommandAttribute
            && (commandKind == CommandKindModel.Scalar || commandKind == CommandKindModel.Identity)
            && IsSupportedReturnType(method.ReturnType))
        {
            diagnostics.Add(new DiagnosticInfo(
                DescriptorId: "ZAO002",
                Location: LocationInfo.From(methodSyntax.ReturnType.GetLocation()),
                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                    method.Name,
                    method.ReturnType.ToDisplayString()))));
        }

        // ZAO032 — MultiResultSet tuple arity exceeds the SQL statement count. Detection
        // ran fine (the tuple itself is classifiable), but the SQL has fewer SELECTs
        // than the tuple requires; the runtime would attempt to read past the last
        // result set and fail. Surface at generation time so the adopter either adds
        // the missing SELECT(s) or trims the tuple. Error severity skips emit via the
        // standard hadError gate.
        //
        // v0.4 Phase D.3 — suppress for [StoredProcedure]. Sprocs carry empty SQL
        // (statementCount == 0) because the procedure body lives server-side; the
        // tuple arity vs statement-count comparison is meaningless here. The
        // adopter's contract is "the sproc produces N result sets matching the
        // tuple arity"; we can't verify that at compile time without parsing the
        // sproc body. Defer arity validation to runtime (the materializer's
        // NextResultAsync call will throw a clear error if a result set is missing).
        if (shape == EmitShape.MultiResultSet && multiResultMaterialization is not null && !isStoredProcedureAttribute)
        {
            var tupleArity = multiResultMaterialization.Elements.Values.Length;
            var statementCount = SqlStatementSplitter.CountStatements(sql);
            if (tupleArity > statementCount)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DescriptorId: "ZAO032",
                    Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                    MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                        method.Name,
                        tupleArity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        statementCount.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
            }
            // ZAO033 — inverse of ZAO032. Extra SELECTs would be silently dropped on
            // the floor by the materializer; surface the mismatch so the adopter
            // either widens the tuple or trims the SQL.
            else if (statementCount > tupleArity)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DescriptorId: "ZAO033",
                    Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                    MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                        method.Name,
                        statementCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        tupleArity.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
            }
        }

        // v0.4 Phase F.3 — ZAO062: [StoredProcedure] named-tuple has at least
        // one field matching a parameter (the SprocWithOutputParams shape was
        // selected) AND at least one OTHER field that does NOT match a
        // parameter. The non-matching field is treated as a result column,
        // which is a legitimate shape (multi-result + output) — so the
        // diagnostic is a WARNING, not an error. The common author mistake it
        // catches is a typo: tuple field `Tota1` instead of `Total` silently
        // demotes an intended output parameter to a result column at runtime.
        // Surfacing the field name in the message lets the adopter spot it.
        //
        // Phase F review Fix 1 — Heuristic 1 (skip the first non-matching
        // tuple field). The canonical sproc-with-outputs pattern is
        // `(OrderRow Result, int NewOrderId)` where `Result` is the
        // conventional result-row position name. Firing ZAO062 on that
        // canonical shape gives every adopter a warning on their first
        // sproc-with-outputs — pure noise. Treat the FIRST non-matching field
        // as the conventional result-row position (intentionally named
        // `Result` / `Row` / `Data` etc.) and skip it. Fire ZAO062 only on the
        // SECOND-and-later non-matching field, which is much more likely a
        // typo than a legitimate result position.
        //
        // The typo case `(int NewOrderId, int Tota1)` with parameter `total`
        // still fires on `Tota1` because there's only one non-matching field
        // and... wait — actually no, in the typo case the FIRST non-matching
        // is `Tota1` which now gets skipped. Hmm. The Heuristic 1 trade-off:
        // we lose the single-typo case but we win the canonical shape. The
        // multi-typo case (two or more non-matching fields) still fires on
        // index 2+. Document the false-negative in docs/diagnostics/ZAO062.md.
        //
        // Phase F review Fix 2 — anchor each ZAO062 at the specific tuple-
        // element syntax rather than the whole return type. Stacking
        // diagnostics at the same span confuses IDEs (dedupe vs multi-squiggle
        // is inconsistent). LocationInfo is cache-safe (FilePath + spans), so
        // we can compute per-element spans here at the syntax-bound diagnostic
        // emit site without leaking Roslyn symbols into the model.
        if (shape == EmitShape.SprocWithOutputParams
            && sprocOutputParamsMaterialization is not null
            && sprocOutputParamsMaterialization.ResultElements.Values.Length > 0)
        {
            // Walk the return-type syntax to locate the tuple's element-syntax
            // nodes. `methodSyntax.ReturnType` is typically `Task<TupleType>` /
            // `ValueTask<TupleType>`; the tuple may also be wrapped in a
            // nullable annotation. Use DescendantNodesAndSelf so we find the
            // tuple regardless of wrapper depth. The tuple-element syntax
            // ordering matches the symbol's TupleElements ordering, which is
            // the same order we walked when building ResultElements, so
            // resultElement index N corresponds to the N-th Result slot in
            // TupleElementOrder.
            var tupleSyntax = methodSyntax.ReturnType
                .DescendantNodesAndSelf()
                .OfType<TupleTypeSyntax>()
                .FirstOrDefault();

            // Map declaration-order tuple-element-index -> result-element-index
            // by walking TupleElementOrder; we need to anchor each ZAO062 at
            // the tuple-element syntax in declaration order, not at the
            // ResultElements position.
            var resultDeclarationIndices = new List<int>(
                sprocOutputParamsMaterialization.ResultElements.Values.Length);
            for (var i = 0; i < sprocOutputParamsMaterialization.TupleElementOrder.Values.Length; i++)
            {
                if (sprocOutputParamsMaterialization.TupleElementOrder.Values[i].Kind
                    == SprocTupleSlotKind.Result)
                {
                    resultDeclarationIndices.Add(i);
                }
            }

            // Heuristic 1: skip the first non-matching field. Start at index 1.
            for (var rIdx = 1; rIdx < sprocOutputParamsMaterialization.ResultElements.Values.Length; rIdx++)
            {
                var resultElement = sprocOutputParamsMaterialization.ResultElements.Values[rIdx];
                var declarationIndex = resultDeclarationIndices[rIdx];

                // Per-element anchor when tuple syntax + index align; fall back
                // to the whole return type if the syntax shape is unexpected
                // (e.g. the tuple lives behind an alias the descendant walk
                // didn't pick up). The fallback path keeps the diagnostic
                // visible rather than dropping it on a syntactic oddity.
                Location elementLocation;
                if (tupleSyntax is not null && declarationIndex < tupleSyntax.Elements.Count)
                {
                    elementLocation = tupleSyntax.Elements[declarationIndex].GetLocation();
                }
                else
                {
                    elementLocation = methodSyntax.ReturnType.GetLocation();
                }

                diagnostics.Add(new DiagnosticInfo(
                    DescriptorId: "ZAO062",
                    Location: LocationInfo.From(elementLocation),
                    MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                        method.Name,
                        resultElement.TupleFieldName))));
            }
        }

        // ZAO022 / ZAO040 — split the "Unknown emit shape" case into two distinct
        // diagnostics:
        //
        //   * ZAO040 (Error)  -- the element type itself has no construction strategy
        //                        (no [ValueObject], no static factory, no single-arg
        //                        ctor, no multi-arg ctor with named-param convention,
        //                        no enum, no primitive). This is an adopter-facing
        //                        modeling problem — they need to add a factory.
        //   * ZAO022 (Info)   -- the element type IS classifiable (ConventionDiscovery
        //                        returns a known Kind) but the surrounding return-type
        //                        shape (e.g. multi-result tuple) isn't yet emittable.
        //                        Pure "generator hasn't shipped this yet" noise.
        //
        // ZAO007 covers IAsyncEnumerable<T>-without-EnumeratorCancellation separately,
        // so we suppress both ZAO022 and ZAO040 for IAsyncEnumerable to avoid the
        // double-diagnostic case.
        //
        // v0.4 Phase A.1: also suppress ZAO022/ZAO040 for [Command]-attributed methods
        // — they intentionally short-circuit to Unknown in Phase A.1 so the emit
        // dispatch surfaces them via the [Command]-aware fallback in EmitRepository.
        // ZAO022/ZAO040 are scoped to [Query] return-shape gaps; firing them for
        // [Command] would mislead the adopter.
        // Post-review Fix 4 — when the classifier already fired a factory-
        // specific diagnostic (ZAO043 / ZAO044 / ZAO051) and bailed to Unknown,
        // ZAO022 / ZAO040 would be a misleading double-report on the same
        // defect. Suppress both in that case.
        // v0.5 Phase E.1 — ZAO052 (recursive composite) belongs to the same
        // class: it specialises the "composite shape can't be lowered yet"
        // case that would otherwise surface as ZAO022.
        var hadFactoryDiagnostic = false;
        for (var di = 0; di < diagnostics.Count; di++)
        {
            var id = diagnostics[di].DescriptorId;
            if (id == "ZAO043" || id == "ZAO044" || id == "ZAO051" || id == "ZAO052")
            {
                hadFactoryDiagnostic = true;
                break;
            }
        }

        if (shape == EmitShape.Unknown
            && !isCommandAttribute
            && !hadFactoryDiagnostic
            && IsSupportedReturnType(method.ReturnType)
            && !IsIAsyncEnumerable(method.ReturnType))
        {
            var elementType = TryGetReturnElementType(method.ReturnType);
            // ZAO040 is reserved for the "single-row materialization target with no
            // construction strategy" case. Container shapes like `Task<List<T>>` or
            // `Task<IEnumerable<T>>` are SHAPE issues (the generator hasn't shipped
            // the multi-row template yet) and remain ZAO022. We discriminate by
            // checking that the element type is non-generic — generics-as-element
            // imply a container shape, not a missing factory on the element type.
            var elementKind = elementType is not null
                ? ConventionDiscovery.Resolve(elementType, conventionContext).Kind
                : ConventionKind.Unknown;
            var elementIsContainerShape = elementType is INamedTypeSymbol en && en.IsGenericType;
            if (elementType is not null
                && elementKind == ConventionKind.Unknown
                && !elementIsContainerShape)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DescriptorId: "ZAO040",
                    Location: LocationInfo.From(methodSyntax.ReturnType.GetLocation()),
                    MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                        elementType.ToDisplayString()))));
            }
            else
            {
                diagnostics.Add(new DiagnosticInfo(
                    DescriptorId: "ZAO022",
                    Location: LocationInfo.From(methodSyntax.ReturnType.GetLocation()),
                    MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                        method.Name,
                        method.ReturnType.ToDisplayString()))));
            }
        }

        // Capture ALL parameters (including CancellationToken) in declaration order so
        // the emit can render the partial method signature verbatim. If we filtered CT
        // out and appended it at emit time, declarations like `(CancellationToken ct,
        // int id)` would produce mismatched partials (CS8795/CS0759). We also track
        // the CT parameter NAME — the body needs to forward the user's named CT to
        // `OpenAsync(...)`, `ReadAsync(...)`, etc., not a hardcoded `ct`.
        //
        // Parameter BINDING to the command (e.g. `@id <- id`) is Phase 6 — for now we
        // only need the signature shape and the CT-name forwarding.
        var parameterDisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        var methodParameters = method.Parameters
            .Select(p =>
            {
                var isCt = string.Equals(p.Type.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal);
                var paramNameOverride = ReadParamNameOverride(p);
                // Nullability detection mirrors the FlatRow column-binding logic:
                // either an annotated nullable reference type (`string?`) or the
                // `Nullable<T>` value-type wrapper (`int?` / `Nullable<int>`).
                var isNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated
                    || (p.Type is INamedTypeSymbol pn
                        && pn.IsGenericType
                        && pn.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T);

                // Convention discovery for parameter binding — the binding emit
                // unwraps ValueObject / SingleArgCtor / StaticFactory parameters via
                // their `Value` property before assigning to DbParameter.Value. CT
                // parameters skip discovery (they're a control signal, not a SQL
                // value). Primitives produce a null ConventionInfo so the existing
                // `@id` emit path is byte-identical to v0.1.
                ConventionInfo? paramConvention = null;
                EquatableArray<CompositeBindingField> compositeFields = default;
                string? compositeTypeFullName = null;
                if (!isCt)
                {
                    var underlying = UnwrapNullableValueType(p.Type);
                    var resolution = ConventionDiscovery.Resolve(underlying, conventionContext);

                    // v0.5 Phase B — composite parameter (`Money(decimal Amount,
                    // string Currency)` etc.). The MultiArgCtor convention reaches
                    // its own binding branch — N DbParameters, one per ctor arg,
                    // named `@{paramName}_{ctorArgName}`. Detection here threads
                    // the per-field model into ParameterInfo so the emit helper
                    // can render the unpacking blocks without re-resolving symbols.
                    //
                    // v0.5 Phase C — nullable composite parameters (`Money? total`)
                    // are now in scope (Option A: bind side mirrors the read side).
                    // When the parameter is null, every inner DbParameter binds
                    // DBNull; when non-null, the existing positional unpacking
                    // runs. ZAO050 fires for the parameter position so adopters
                    // see the runtime all-or-nothing contract.
                    //
                    // Post-review Fix 1 — the nullable-composite bind emit uses
                    // `@param.Value.@field` to reach inner fields on the non-null
                    // branch. `.Value` only exists on `Nullable<T>` (value-type
                    // composite); a reference-type composite declared `OrderRow?`
                    // doesn't have `.Value` and would produce CS1061 in adopter
                    // code. Mirror the read-side `compositeIsNullableValueType`
                    // gate: only `Nullable<T>` struct composites enter the
                    // nullable-composite bind branch. Nullable REFERENCE
                    // composites fall through to ZAO041 (the resolution kind
                    // remains MultiArgCtor but `compositeFields` stays empty
                    // so the ZAO041 sentinel fires). Reference-type composites
                    // need a future enhancement (Option B: branch on class-vs-
                    // struct in the emit, omitting `.Value` for classes).
                    var isCompositeNullableValueType =
                        p.Type is INamedTypeSymbol pcn
                        && pcn.IsGenericType
                        && pcn.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T;
                    if (resolution.Kind == ConventionKind.MultiArgCtor)
                    {
                        // ZAO063 — `[Param(Name = "...")]` cannot compose with the
                        // positional `@{paramName}_{ctorArgName}` unpacking convention.
                        // Surfacing this as a hard error (rather than silently dropping
                        // the override, which was the pre-review behaviour) prevents
                        // adopters from shipping a misleading attribute and discovering
                        // the no-op at runtime via "parameter @custom not found".
                        if (paramNameOverride is not null)
                        {
                            var paramLocation = p.Locations.FirstOrDefault() ?? Location.None;
                            diagnostics.Add(new DiagnosticInfo(
                                DescriptorId: "ZAO063",
                                Location: LocationInfo.From(paramLocation),
                                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                                    p.Name,
                                    method.Name,
                                    paramNameOverride))));
                        }

                        // Nullable reference-type composite is NOT classified as
                        // a supported binding strategy in Phase C — leave
                        // compositeFields empty so the ZAO041 sentinel fires below.
                        var nullableRefComposite = isNullable && !isCompositeNullableValueType;
                        if (!nullableRefComposite)
                        {
                            compositeFields = BuildCompositeFields(resolution, conventionContext);
                            if (compositeFields.Length > 0)
                            {
                                compositeTypeFullName = underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                                // v0.5 Phase C.2 — nullable composite parameter
                                // triggers ZAO050. The diagnostic is at the
                                // parameter location so the squiggle lands on
                                // the user's `Money? total` declaration. Only
                                // value-type `Nullable<T>` reaches this branch
                                // (reference-type composites with `?` fell
                                // through above).
                                if (isCompositeNullableValueType)
                                {
                                    var paramLocation = p.Locations.FirstOrDefault() ?? Location.None;
                                    diagnostics.Add(new DiagnosticInfo(
                                        DescriptorId: "ZAO050",
                                        Location: LocationInfo.From(paramLocation),
                                        MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                                            method.Name,
                                            compositeTypeFullName,
                                            "parameter '" + p.Name + "'"))));
                                }
                            }
                        }
                    }

                    // ZAO041 — no binding strategy resolved for parameter. Fires when the
                    // parameter type doesn't match any convention (no Value, no primitive,
                    // no enum, no static From factory, no single-arg ctor). Keyed at the
                    // parameter symbol's first declaration so the user's squiggle lands on
                    // their parameter, not on the type definition.
                    //
                    // v0.5 Phase B — MultiArgCtor with a successfully-built CompositeFields
                    // list is a known strategy now; the silent-on-MultiArgCtor branch lives
                    // in the `&& compositeTypeFullName is null` clause below. A MultiArgCtor
                    // whose inner fields didn't resolve (or a nullable composite) still
                    // fires ZAO041 because the binding can't proceed.
                    if (resolution.Kind == ConventionKind.Unknown
                        || (resolution.Kind == ConventionKind.MultiArgCtor && compositeTypeFullName is null))
                    {
                        var paramLocation = p.Locations.FirstOrDefault() ?? Location.None;
                        diagnostics.Add(new DiagnosticInfo(
                            DescriptorId: "ZAO041",
                            Location: LocationInfo.From(paramLocation),
                            MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                                p.Name,
                                underlying.ToDisplayString()))));
                    }

                    var underlyingReader = resolution.Kind switch
                    {
                        ConventionKind.ValueObject or ConventionKind.SingleArgCtor or ConventionKind.StaticFactory
                            => ResolveUnderlyingReaderForFactory(resolution),
                        // Enum: the binding emit casts to the underlying integral type
                        // (typically int). We capture the matching reader for symmetry
                        // with the materialization path so the column emit can pull a
                        // single source of truth.
                        ConventionKind.Enum => ResolveUnderlyingReaderForEnum(underlying),
                        // [StoreAsString] enums round-trip as strings; reader is
                        // GetString and the binding emit calls `.ToString()`.
                        ConventionKind.EnumAsString => "GetString",
                        _ => null,
                    };
                    paramConvention = BuildConventionInfo(underlying, resolution, underlyingReader);
                }

                return new ParameterInfo(
                    p.Name,
                    p.Type.ToDisplayString(parameterDisplayFormat),
                    isCt,
                    paramNameOverride,
                    isNullable,
                    paramConvention,
                    compositeFields,
                    compositeTypeFullName);
            })
            .ToImmutableArray();
        var cancellationTokenParameterName = methodParameters
            .FirstOrDefault(p => p.IsCancellationToken)?.Name;

        // Fully-qualified, includes `?` for nullable reference types so the emitted
        // partial signature matches the user's partial declaration verbatim.
        var returnTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        var returnTypeDisplay = method.ReturnType.ToDisplayString(returnTypeFormat);

        var methodModel = new QueryMethodModel(
            MethodName: method.Name,
            ContainingTypeFullName: containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Sql: sql,
            Shape: shape,
            Strategy: strategy,
            ReturnTypeDisplay: returnTypeDisplay,
            NullableScalarReaderMethod: nullableReaderMethod,
            Materialization: materialization,
            MultiResultMaterialization: multiResultMaterialization,
            MethodParameters: new EquatableArray<ParameterInfo>(methodParameters),
            CancellationTokenParameterName: cancellationTokenParameterName,
            Diagnostics: new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutable()),
            // [Query] pipeline passes (false, NonQuery); [Command] passes (true, kind).
            // Threading both explicitly (no defaults) keeps the call site self-documenting
            // and forces future pipeline additions (e.g. [StoredProcedure]) to make a
            // deliberate decision.
            IsCommand: isCommandAttribute,
            CommandKind: commandKind,
            HasReturnValue: hasReturnValue,
            // v0.4 Phase D — Stored-procedure pipeline thread-through. The
            // emit shapes consult these to flip CommandText -> ProcedureName
            // and emit `CommandType = StoredProcedure`. [Query]/[Command]
            // pipelines pass (false, ""). The ProcedureName is the literal
            // first ctor arg of the [StoredProcedure] attribute, used
            // verbatim as the CommandText.
            IsStoredProcedure: isStoredProcedureAttribute,
            ProcedureName: procedureName,
            // v0.4 Phase E — populated only for EmitShape.SprocWithOutputParams.
            // Null for every other shape (and for sprocs falling through to
            // the MultiResultSet shape because no tuple field matched a C#
            // parameter). The Phase E.2/E.3 emit consumes this model directly;
            // earlier shapes ignore it.
            SprocOutputParamsMaterialization: sprocOutputParamsMaterialization,
            // v1.2 — re-emit the user's declared accessibility verbatim. CS8799
            // requires both partial declarations to match; hardcoding `public`
            // here forced adopters to expose internal helper methods on the
            // public surface (surfaced via ZA.Templates PR #152 — issue #101).
            MethodAccessibilityKeyword: MethodAccessibilityKeyword(method.DeclaredAccessibility),
            // v1.3 — populated only for EmitShape.BulkInsertCommand by
            // ClassifyBulkInsertCommand. Null for every other shape; Task 6's
            // emit path reads it to render the chunked VALUES INSERT.
            BulkInsertMaterialization: bulkInsertMaterialization);

        return new QueryMethodWithTypeContext(
            Method: methodModel,
            ContainingTypeName: containing.Name,
            Namespace: containing.ContainingNamespace.IsGlobalNamespace
                ? null
                : containing.ContainingNamespace.ToDisplayString(),
            ConnectionAccess: connectionAccess,
            ConnectionResolved: connectionResolved,
            ContainingTypePartial: containingTypePartial,
            ContainingTypeLocation: containingTypeLocation);
    }

    // Decide which emit template fits this method. Conservative on purpose:
    // Map Microsoft.CodeAnalysis.Accessibility to its C# keyword form so the
    // generator can re-emit the user's declared modifier verbatim on the
    // partial implementation. CS8799 fires when the two partial declarations
    // disagree on accessibility, so the emit *must* match exactly. Issue #101.
    private static string MethodAccessibilityKeyword(Accessibility accessibility) =>
        accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            // C# spec: `protected` + `internal` (union) is `protected internal`;
            // `protected` ∩ `internal` (intersection, "in same project AND derived")
            // is `private protected`. Roslyn's `ProtectedOrInternal` is the union,
            // `ProtectedAndInternal` the intersection. Easy to flip these two by
            // accident — see the doc comment on Accessibility for the canonical
            // mapping.
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.Private => "private",
            // NotApplicable defaults to private for the user's safety. Reaching
            // this branch implies a Roslyn symbol with no declared accessibility,
            // which should be impossible for a method symbol coming through the
            // partial-method pipeline. The fall-through keeps emit deterministic.
            _ => "private",
        };

    // returns concrete shapes only for the exact v0.1 Phase-4 templates with a
    // single CancellationToken parameter (no user-bound parameters). Everything
    // else stays Unknown and falls through to the stub-comment path until a later
    // Phase 4 task adds its template.
    //
    // For NullableScalar we also return the IDataReader.GetXxx method name to use
    // at emit time so EmitNullableScalar doesn't need to re-derive it from a model
    // that no longer carries the symbol.
    private static (EmitShape Shape, string? NullableReaderMethod, MaterializationModel? Materialization, MultiResultMaterializationModel? MultiResultMaterialization, bool HasReturnValue, SprocOutputParamsMaterializationModel? SprocOutputParams, BulkInsertMaterializationModel? BulkInsertMaterialization) ClassifyEmitShape(
        IMethodSymbol method,
        ConventionContext conventionContext,
        bool isCommandAttribute,
        CommandKindModel commandKind,
        bool isStoredProcedureAttribute,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        LocationInfo? returnTypeLocation,
        string sql)
    {
        // v0.4 Phase A.2 — [Command(Kind = NonQuery)] dispatch. Accepts:
        //   * Task<int>, ValueTask<int>  — return rows-affected count.
        //   * Task, ValueTask            — fire-and-await (no return value).
        // Other return shapes (Task<string>, Task<MyClass>, etc.) on a NonQuery
        // command fall through to Unknown so the existing emit stub (in
        // EmitRepository's default branch) keeps the consumer's build alive while
        // surfacing the missing-implementation as a runtime throw.
        //
        // Scalar (Phase B) and Identity (Phase C) kinds are NOT classified in
        // Phase A; they fall through to Unknown and route to the [Command] stub
        // emit path. The stub message names the kind so adopters know which
        // milestone covers their shape.
        if (isCommandAttribute && commandKind == CommandKindModel.NonQuery)
        {
            if (method.ReturnType is INamedTypeSymbol cmdReturn)
            {
                var name = cmdReturn.Name;
                // Task<int> / ValueTask<int>
                if (cmdReturn.Arity == 1
                    && (name == "Task" || name == "ValueTask")
                    && cmdReturn.TypeArguments.Length == 1
                    && cmdReturn.TypeArguments[0].SpecialType == SpecialType.System_Int32)
                {
                    return (EmitShape.CommandNonQuery, null, null, null, HasReturnValue: true, SprocOutputParams: null, BulkInsertMaterialization: null);
                }
                // Task / ValueTask (no result)
                if (cmdReturn.Arity == 0 && (name == "Task" || name == "ValueTask"))
                {
                    return (EmitShape.CommandNonQuery, null, null, null, HasReturnValue: false, SprocOutputParams: null, BulkInsertMaterialization: null);
                }
            }
            return (EmitShape.Unknown, null, null, null, HasReturnValue: false, SprocOutputParams: null, BulkInsertMaterialization: null);
        }

        // v0.4 Phase B — [Command(Kind = Scalar)] dispatch. Extracted to
        // ClassifyCommandScalar so this method stays focused on the shape-table.
        if (isCommandAttribute && commandKind == CommandKindModel.Scalar)
        {
            return ClassifyCommandScalar(method, conventionContext);
        }

        // v0.4 Phase C — [Command(Kind = Identity)] dispatch. Extracted to
        // ClassifyCommandIdentity for symmetry with the Scalar branch. Identity
        // narrows the accepted return types (int / long / Guid + value-objects
        // wrapping those) and rejects nullable shapes — see the helper's doc.
        if (isCommandAttribute && commandKind == CommandKindModel.Identity)
        {
            return ClassifyCommandIdentity(method, conventionContext);
        }

        // v1.3 — [Command(Kind = BulkInsert)] dispatch. Recognizes a single
        // IEnumerable<TRow>-shaped collection parameter, a VALUES (@p1, ...)
        // tuple in the SQL, and one of two return shapes (Task<int> rows-affected
        // or Task<IReadOnlyList<TIdentity>> identity buffer). All four shape
        // checks are hard ZAO070-073 errors when violated. ClassifyBulkInsertCommand
        // returns BulkInsertCommand + a fully-populated materialization model on
        // success; on any failure it returns Unknown and the diagnostic blocks
        // emit. Task 6 will replace the v1.3 stub emit path with the real chunked
        // VALUES emit.
        if (isCommandAttribute && commandKind == CommandKindModel.BulkInsert)
        {
            var bulkShape = ClassifyBulkInsertCommand(
                method,
                sql,
                conventionContext,
                diagnostics,
                returnTypeLocation,
                out var bulkInsertMaterialization);
            // Materialization is threaded back to the caller via the tuple's
            // MultiResultMaterialization slot would be wrong (that's a separate
            // model). Use a new return-tuple position so the caller can wire it
            // into QueryMethodModel.BulkInsertMaterialization.
            return (
                bulkShape,
                null,
                null,
                null,
                HasReturnValue: bulkShape != EmitShape.Unknown,
                SprocOutputParams: null,
                BulkInsertMaterialization: bulkInsertMaterialization);
        }

        if (isCommandAttribute)
        {
            // Defensive — all four CommandKind values (NonQuery / Scalar /
            // Identity / BulkInsert) have explicit dispatch above. Reaching this
            // point means a new CommandKindModel value was added without an
            // accompanying dispatch update; failing loudly here keeps the
            // generator's shape table honest instead of silently routing to
            // Unknown → ZAO002.
            throw new global::System.InvalidOperationException(
                $"Unhandled CommandKindModel value '{commandKind}' — generator dispatch is incomplete.");
        }

        if (method.ReturnType is not INamedTypeSymbol named) return (EmitShape.Unknown, null, null, null, HasReturnValue: false, SprocOutputParams: null, BulkInsertMaterialization: null);

        // v0.3 Phase C — IAsyncEnumerable<T> streaming. Match by metadata name + arity
        // and require the element type to resolve to a row-shaped materialization
        // (FlatRow or DomainEntity). ZAO007 separately covers the missing
        // [EnumeratorCancellation] case; here we only classify the shape.
        if (string.Equals(named.MetadataName, "IAsyncEnumerable`1", StringComparison.Ordinal)
            && named.Arity == 1
            && named.TypeArguments.Length == 1
            && string.Equals(named.ContainingNamespace?.ToDisplayString(), "System.Collections.Generic", StringComparison.Ordinal))
        {
            var streamElement = named.TypeArguments[0].WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            var streamFlat = TryBuildFlatRowMaterialization(streamElement, conventionContext);
            if (streamFlat is not null)
                return (EmitShape.Streaming, null, streamFlat, null, HasReturnValue: true, SprocOutputParams: null, BulkInsertMaterialization: null);
            var streamDomain = TryBuildDomainEntityMaterialization(streamElement, conventionContext);
            if (streamDomain is not null)
                return (EmitShape.Streaming, null, streamDomain, null, HasReturnValue: true, SprocOutputParams: null, BulkInsertMaterialization: null);
            // Element type not classifiable — fall through to Unknown so the existing
            // ZAO022 / ZAO040 path surfaces the gap. ZAO007 still fires upstream when
            // the user forgets [EnumeratorCancellation].
            return (EmitShape.Unknown, null, null, null, HasReturnValue: false, SprocOutputParams: null, BulkInsertMaterialization: null);
        }

        // Restrict to Task<T> for now; ValueTask<T> lands later.
        if (!(named.Name == "Task" && named.Arity == 1)) return (EmitShape.Unknown, null, null, null, HasReturnValue: false, SprocOutputParams: null, BulkInsertMaterialization: null);

        var inner = named.TypeArguments[0];

        // v1.2 — Task<IReadOnlyList<TRow>>: bare top-level list return. Single
        // result set drained into a buffered List<TRow>. Distinct from
        // MultiResultSet (which uses a tuple shape with List as one element kind)
        // and from Streaming (IAsyncEnumerable, yield-based, no buffering).
        // Element materialization reuses the FlatRow / DomainEntity models so
        // positional records and named-column classes both work. Issue #102.
        if (inner is INamedTypeSymbol listInner
            && string.Equals(listInner.MetadataName, "IReadOnlyList`1", StringComparison.Ordinal)
            && listInner.Arity == 1
            && listInner.TypeArguments.Length == 1
            && string.Equals(listInner.ContainingNamespace?.ToDisplayString(), "System.Collections.Generic", StringComparison.Ordinal))
        {
            var listElement = listInner.TypeArguments[0].WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            var listFlat = TryBuildFlatRowMaterialization(listElement, conventionContext);
            if (listFlat is not null)
                return (EmitShape.ListResultSet, null, listFlat, null, HasReturnValue: true, SprocOutputParams: null, BulkInsertMaterialization: null);
            var listDomain = TryBuildDomainEntityMaterialization(listElement, conventionContext);
            if (listDomain is not null)
                return (EmitShape.ListResultSet, null, listDomain, null, HasReturnValue: true, SprocOutputParams: null, BulkInsertMaterialization: null);
            // Element type not classifiable — fall through to Unknown so the
            // existing ZAO022 / ZAO040 path surfaces the gap.
            return (EmitShape.Unknown, null, null, null, HasReturnValue: false, SprocOutputParams: null, BulkInsertMaterialization: null);
        }

        // v0.3 Phase B — tuple return type (with optional outer nullable) flagged as
        // MultiResultSet. Element shapes (scalar / single row / list) are captured on
        // MultiResultMaterializationModel so the emit can choose its per-element loop.
        // We peel `Nullable<T>` here as well as the `T?` reference-annotation form
        // because `Task<(...)?>` surfaces either way depending on how the user writes
        // the tuple.
        var tupleCandidate = UnwrapNullableValueType(inner);
        var tupleReturnsNullable = inner.NullableAnnotation == NullableAnnotation.Annotated
            || !ReferenceEquals(tupleCandidate, inner);
        if (tupleCandidate is INamedTypeSymbol tupleNamed && tupleNamed.IsTupleType && tupleNamed.TupleElements.Length >= 2)
        {
            // v0.4 Phase E.1 — [StoredProcedure] tuple-return with named output
            // parameters takes precedence over the regular MultiResultSet
            // classification. Detection: at least one tuple field name matches
            // (case-insensitive) a C# parameter name. The Result positions (no
            // matching parameter) are classified via the same MultiResultElement
            // rules MultiResultSet uses, so a single-result-row + output-param
            // shape and a multi-result-set + output-param shape both flow
            // through the same SprocWithOutputParams emit.
            //
            // Restriction to [StoredProcedure]: output parameters only make
            // sense with CommandType.StoredProcedure. [Query] / [Command]
            // methods returning a tuple stay on the MultiResultSet path; a
            // tuple field name happening to match a parameter is coincidence
            // there and we don't want to silently rebind it as Direction.Output.
            if (isStoredProcedureAttribute)
            {
                // v0.4 Phase E review Fix 2 — reject nullable-outer tuples on
                // sproc-with-output-params shapes. The semantics of "empty first
                // result set => return null" are unclear when output parameters
                // are also part of the contract: should `Parameter.Value` be
                // observed or skipped? Rather than guess, we surface the
                // unsupported-shape diagnostic (ZAO022) and require the adopter
                // to either drop the outer nullable annotation or move to a
                // pure-MultiResultSet shape (no output params). The check fires
                // ONLY when at least one tuple field name matches a C# parameter
                // — otherwise the tuple stays on the MultiResultSet path (which
                // legitimately supports nullable-outer). Adopter-demand for the
                // nullable-output-params case is tracked in the v0.4-CLN
                // backlog.
                if (tupleReturnsNullable
                    && SprocHasAnyOutputParamMatch(tupleNamed, method.Parameters))
                {
                    return (EmitShape.Unknown, null, null, null, HasReturnValue: false, SprocOutputParams: null, BulkInsertMaterialization: null);
                }
                var sprocOutputs = TryBuildSprocOutputParamsMaterialization(
                    tupleNamed, tupleReturnsNullable, method.Parameters, conventionContext);
                if (sprocOutputs is not null)
                    return (EmitShape.SprocWithOutputParams, null, null, null, HasReturnValue: true, SprocOutputParams: sprocOutputs, BulkInsertMaterialization: null);
            }

            var multi = TryBuildMultiResultMaterialization(tupleNamed, tupleReturnsNullable, conventionContext);
            if (multi is not null)
                return (EmitShape.MultiResultSet, null, null, multi, HasReturnValue: true, SprocOutputParams: null, BulkInsertMaterialization: null);
            // Tuple shape but at least one element wasn't classifiable: fall through to
            // Unknown so ZAO022 (or a future ZAO032/033) surfaces the gap.
        }

        // Scalar shapes now tolerate user parameters at the signature level — Phase 6
        // emits the binding loop so the SQL placeholders resolve at runtime. FlatRow
        // followed the same path in Task 5.1.

        // Nullable reference: Task<string?> (and any other supported nullable primitive).
        if (inner.IsReferenceType && inner.NullableAnnotation == NullableAnnotation.Annotated)
        {
            var readerMethod = GetScalarPrimitiveReaderInfo(inner);
            if (readerMethod is not null)
                return (EmitShape.NullableScalar, readerMethod, null, null, HasReturnValue: true, SprocOutputParams: null, BulkInsertMaterialization: null);
        }

        // Nullable value type: Task<int?> / Task<Nullable<T>>.
        if (inner is INamedTypeSymbol innerNamed
            && innerNamed.IsGenericType
            && innerNamed.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T)
        {
            var underlying = innerNamed.TypeArguments[0];
            var readerMethod = GetScalarPrimitiveReaderInfo(underlying);
            if (readerMethod is not null)
                return (EmitShape.NullableScalar, readerMethod, null, null, HasReturnValue: true, SprocOutputParams: null, BulkInsertMaterialization: null);
        }

        // Non-nullable int (Task 4.1) — kept as a separate shape so the existing
        // ExecuteScalarAsync emit + snapshot stays unchanged in v0.1.
        if (inner.SpecialType == SpecialType.System_Int32) return (EmitShape.ScalarInt, null, null, null, HasReturnValue: true, SprocOutputParams: null, BulkInsertMaterialization: null);

        // v0.5 Phase D — `[Materialize(Factory = "X")]` resolution. Per design
        // Section 3, line 260 (discovery-order rule #1), an explicit factory
        // annotation ALWAYS wins over convention discovery. Extracted into
        // ResolveScalarFactoryShape (post-review Fix 6) to keep ClassifyEmitShape
        // closer to readable. Returns non-null when EITHER a factory branch was
        // taken (success or failure with ZAO043/051) OR the [Materialize(Strategy
        // = Custom)] without-Factory diagnostic fired (Fix 8); null leaves the
        // surrounding classifier to fall through to the composite / FlatRow /
        // DomainEntity branches.
        {
            var scalarFactoryShape = ResolveScalarFactoryShape(
                method, inner, conventionContext, diagnostics, returnTypeLocation);
            if (scalarFactoryShape is { } sfs) return sfs;
        }

        // v0.5 Phase A — composite at scalar position. `Task<Money>` where
        // `Money(decimal Amount, string Currency)` constructs the composite type
        // directly from the SELECT list. Tried BEFORE FlatRow's nullable-reference
        // branch so a non-nullable composite return shape funnels here (records
        // with a single-arg ctor were already classified as SingleArgCtor upstream;
        // multi-arg ctor types reach MultiArgCtor only when they have > 1 ctor params).
        //
        // The classifier intentionally excludes nullable-annotated reference types
        // (e.g. `Task<OrderRow?>` where OrderRow is a record class) — those route
        // to FlatRow / DomainEntity for outer-level row construction. Composite
        // detection is reserved for VALUE-TYPE composites at scalar position;
        // record / class types reach the FlatRow path first regardless of arity.
        //
        // v0.5 Phase C — Task<Money?> (Nullable<Money> at scalar position) is
        // routed here when Money is a value-type composite. The
        // MaterializationModel.IsNullable flag toggles EmitComposite into the
        // all-or-nothing branch (return null on all-DBNull, throw on mixed-null).
        // Nullable REFERENCE composite (a class with N-arg ctor declared as
        // `Task<TClass?>`) stays on the FlatRow / DomainEntity path so the
        // existing empty-row -> null behaviour is preserved; the FlatRow /
        // DomainEntity emit handles nullable composite ctor-params nested
        // inside (one level deeper) via the hoisted-local pattern.
        if (!inner.IsReferenceType)
        {
            var compositeIsNullableValueType =
                inner is INamedTypeSymbol cn
                && cn.IsGenericType
                && cn.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T;
            var compositeCandidate = UnwrapNullableValueType(inner)
                .WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            var composite = TryBuildCompositeMaterialization(compositeCandidate, conventionContext);
            if (composite is not null)
            {
                var withNullable = compositeIsNullableValueType
                    ? composite with { IsNullable = true }
                    : composite;
                return (EmitShape.Composite, null, withNullable, null, HasReturnValue: true, SprocOutputParams: null, BulkInsertMaterialization: null);
            }

            // v0.5 Phase E.1 — ZAO052: the composite-at-scalar path bailed,
            // probe for the recursive-composite shape (outer is MultiArgCtor-
            // shaped but at least one inner ctor param is itself MultiArgCtor).
            // ConventionDiscovery returns Unknown for the outer in that case,
            // so the regular flow falls through to ZAO022 with a generic
            // message. ZAO052 surfaces the specific cause + workaround.
            if (TryDetectRecursiveCompositeInner(compositeCandidate, conventionContext) is { } recursive)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DescriptorId: "ZAO052",
                    Location: returnTypeLocation,
                    MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                        method.Name,
                        recursive.OuterTypeDisplay,
                        recursive.InnerCtorArgName))));
                return (EmitShape.Unknown, null, null, null, HasReturnValue: false, SprocOutputParams: null, BulkInsertMaterialization: null);
            }
        }

        // FlatRow (Task 5.1) — positional record with ctor params resolvable to known
        // conventions. v0.2 Phase C extends column resolution from "primitive only" to
        // anything ConventionDiscovery surfaces (ValueObject, SingleArgCtor, StaticFactory).
        // v0.1 only handles Task<T?> (nullable reference) so an empty result set maps
        // cleanly to `return null;`. Non-nullable Task<T> would require an exception
        // on empty and is deferred to v0.2.
        if (inner.IsReferenceType && inner.NullableAnnotation == NullableAnnotation.Annotated)
        {
            // Peel the nullable annotation so we look at the underlying record type.
            var elementType = inner.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            var flat = TryBuildFlatRowMaterialization(elementType, conventionContext, diagnostics, method.Name, returnTypeLocation);
            if (flat is not null)
                return (EmitShape.FlatRow, null, flat, null, HasReturnValue: true, SprocOutputParams: null, BulkInsertMaterialization: null);

            // v0.2 Phase E — DomainEntity: a non-record class with a single public
            // ctor whose parameters all resolve to known conventions. The detection
            // sits AFTER FlatRow so record types continue to take the positional path
            // (record ctor params have synthesized properties on the type, making
            // them ambiguous between FlatRow-positional and DomainEntity-named; the
            // positional path stays the default for records).
            var domain = TryBuildDomainEntityMaterialization(elementType, conventionContext, diagnostics, method.Name, returnTypeLocation);
            if (domain is not null)
                return (EmitShape.DomainEntity, null, domain, null, HasReturnValue: true, SprocOutputParams: null, BulkInsertMaterialization: null);
        }

        return (EmitShape.Unknown, null, null, null, HasReturnValue: false, SprocOutputParams: null, BulkInsertMaterialization: null);
    }

    // v0.4 Phase B — [Command(Kind = Scalar)] dispatch. Accepts any return type
    // shape that reduces to ExecuteScalarAsync's single-value result:
    //   * Task<TPrimitive>      — Convert.ToXxx funnel from object?.
    //   * Task<TPrimitive?>     — DBNull/null guard before the cast.
    //   * Task<TValueObject>    — factory wrap (ValueObject / SingleArgCtor /
    //                              StaticFactory) over the unwrapped primitive.
    //   * Task<TEnum>           — cast to the enum from the underlying integral.
    //   * Task<TEnum?>          — same with DBNull/null guard.
    //
    // Container shapes (List<T>, tuples, IAsyncEnumerable<T>) on a Scalar kind
    // are unsupported. Returning EmitShape.Unknown surfaces ZAO002 at compile time
    // via the diagnostic block in TransformMethod (see the ZAO002-for-Scalar
    // branch); the Phase A runtime stub is the secondary defense if the diagnostic
    // is suppressed.
    private static (EmitShape Shape, string? NullableReaderMethod, MaterializationModel? Materialization, MultiResultMaterializationModel? MultiResultMaterialization, bool HasReturnValue, SprocOutputParamsMaterializationModel? SprocOutputParams, BulkInsertMaterializationModel? BulkInsertMaterialization) ClassifyCommandScalar(
        IMethodSymbol method,
        ConventionContext conventionContext)
        => ClassifyScalarLikeReturn(
            method,
            conventionContext,
            allowNullable: true,
            // Scalar accepts the full ConventionDiscovery family set: every
            // primitive in the catalog, plus value-objects, enums, and
            // string-backed enums. Container / unsupported types fall through
            // to a null reader → Unknown.
            resolveReader: static (unwrapped, resolution) => resolution.Kind switch
            {
                ConventionKind.Primitive => PrimitiveCatalog.GetScalarReaderMethod(unwrapped),
                ConventionKind.ValueObject or ConventionKind.SingleArgCtor or ConventionKind.StaticFactory
                    => ResolveUnderlyingReaderForFactory(resolution),
                ConventionKind.Enum => ResolveUnderlyingReaderForEnum(unwrapped),
                ConventionKind.EnumAsString => "GetString",
                _ => null,
            },
            shape: EmitShape.CommandScalar);

    // v0.4 Phase C — [Command(Kind = Identity)] dispatch. Identity is the
    // "INSERT ... RETURNING Id" / "INSERT ...; SELECT SCOPE_IDENTITY()" shape:
    // ExecuteScalarAsync with a non-null integer / Guid result that becomes the
    // method's return value (optionally wrapped in a value-object).
    //
    // Accepted return shapes (Task<T> / ValueTask<T> only):
    //   * Task<int>      — primitive cast via Convert.ToInt32.
    //   * Task<long>     — primitive cast via Convert.ToInt64.
    //   * Task<Guid>     — direct cast (no Convert.ToGuid in BCL).
    //   * Task<TVO>      — value-object wrapping one of int / long / Guid via
    //                       ConventionDiscovery (ValueObject / SingleArgCtor /
    //                       StaticFactory).
    //
    // REJECTED — fall through to Unknown and ZAO002 fires upstream:
    //   * Nullable shapes (Task<int?>, Task<TVO?>, etc.) — Identity has no
    //     nullable variant per design. The SQL contract requires the RETURNING /
    //     SCOPE_IDENTITY() clause to produce a non-null value.
    //   * Primitives outside the int / long / Guid trio (decimal, string, etc.)
    //     — these don't represent identity keys; route them through Kind=Scalar.
    //   * Container shapes (List<T>, tuples, IAsyncEnumerable<T>) — Identity is
    //     a single-value shape.
    private static (EmitShape Shape, string? NullableReaderMethod, MaterializationModel? Materialization, MultiResultMaterializationModel? MultiResultMaterialization, bool HasReturnValue, SprocOutputParamsMaterializationModel? SprocOutputParams, BulkInsertMaterializationModel? BulkInsertMaterialization) ClassifyCommandIdentity(
        IMethodSymbol method,
        ConventionContext conventionContext)
        => ClassifyScalarLikeReturn(
            method,
            conventionContext,
            // Identity rejects nullable variants — both `T?` reference
            // annotation and `Nullable<T>` value-type wrapper. The SQL contract
            // (RETURNING / SCOPE_IDENTITY()) guarantees a non-null value.
            allowNullable: false,
            // Identity narrows the primitive set to int / long / Guid and
            // rejects Enum / EnumAsString outright — identity keys are
            // structurally an integer or UUID. Value-object / factory paths
            // route through ResolveIdentityUnderlyingReaderForFactory which
            // applies the same primitive-acceptance filter to the wrapped type.
            resolveReader: static (unwrapped, resolution) => resolution.Kind switch
            {
                ConventionKind.Primitive => IsIdentityPrimitive(unwrapped)
                    ? PrimitiveCatalog.GetScalarReaderMethod(unwrapped)
                    : null,
                ConventionKind.ValueObject or ConventionKind.SingleArgCtor or ConventionKind.StaticFactory
                    => ResolveIdentityUnderlyingReaderForFactory(resolution),
                _ => null,
            },
            shape: EmitShape.CommandIdentity);

    // v1.3 — [Command(Kind = BulkInsert)] classifier. Runs four shape checks; any
    // failure adds a hard ZAO070-073 diagnostic and returns EmitShape.Unknown so
    // the surrounding ZAO002 / partial-stub paths don't fire on top of it. On
    // success, populates `materialization` with the full Task 6 input.
    //
    //   1. Exactly one IEnumerable<TRow>-shaped parameter (ignoring CT)         → ZAO070
    //   2. SQL has exactly one VALUES (@p1, ...) tuple                          → ZAO071
    //   3. Every placeholder resolves to a public TRow property (case-insensitive) → ZAO072
    //   4. Return type is Task<int> or Task<IReadOnlyList<TIdentity>> where     → ZAO073
    //      TIdentity is int / long / Guid or a [ValueObject]/factory wrapping one
    //
    // The chunk size is baked at codegen as 900 / placeholderCount (floor 1) —
    // 900 stays under SQL Server's 2100-parameter ceiling with comfortable headroom
    // and matches every other provider's looser limits.
    private static EmitShape ClassifyBulkInsertCommand(
        IMethodSymbol method,
        string sql,
        ConventionContext conventionContext,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        LocationInfo? methodLocation,
        out BulkInsertMaterializationModel? materialization)
    {
        materialization = null;

        // 1. Collection parameter shape. Walk all parameters, skip the CT (a
        //    control signal), and look for IReadOnlyList<T> / IList<T> / IEnumerable<T>
        //    from System.Collections.Generic. The "exactly one" rule keeps the
        //    emit's "one chunk loop over one source" shape unambiguous; zero
        //    or multiple collection parameters fire ZAO070.
        IParameterSymbol? collectionParameter = null;
        INamedTypeSymbol? collectionType = null;
        ITypeSymbol? rowType = null;
        bool isReadOnlyList = false;
        int collectionCandidateCount = 0;
        foreach (var p in method.Parameters)
        {
            if (string.Equals(p.Type.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal))
                continue;
            if (p.Type is not INamedTypeSymbol pNamed
                || pNamed.Arity != 1
                || pNamed.TypeArguments.Length != 1)
                continue;
            if (!string.Equals(
                    pNamed.ContainingNamespace?.ToDisplayString(),
                    "System.Collections.Generic",
                    StringComparison.Ordinal))
                continue;
            // Restrict the BulkInsert-acceptable shapes to the three the emit can
            // straightforwardly chunk. List<T> / ICollection<T> / IReadOnlyCollection<T>
            // are deliberately excluded for v1.3 — they would either need a copy
            // (no random access without materialising) or duplicate the IList path
            // without clear adopter benefit. Adopters with those collection
            // shapes can cast/wrap at the call site.
            var shapeName = pNamed.Name;
            if (shapeName != "IReadOnlyList"
                && shapeName != "IList"
                && shapeName != "IEnumerable")
                continue;
            collectionCandidateCount++;
            if (collectionParameter is null)
            {
                collectionParameter = p;
                collectionType = pNamed;
                rowType = pNamed.TypeArguments[0];
                isReadOnlyList = shapeName == "IReadOnlyList";
            }
        }

        if (collectionCandidateCount != 1 || collectionParameter is null || rowType is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                DescriptorId: "ZAO070",
                Location: methodLocation,
                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                    method.Name,
                    collectionCandidateCount.ToString(global::System.Globalization.CultureInfo.InvariantCulture)))));
            return EmitShape.Unknown;
        }

        // 2. VALUES tuple parse. The parser's TupleCount tells us how many
        //    `(...)` row-tuples the SQL contains; we want exactly one (the
        //    generator's chunk-multiplication adds the others at runtime). Zero
        //    matches and the multi-row case both report via ZAO071 with the
        //    actual TupleCount so the adopter sees the cardinality.
        var valuesResult = BulkInsertValuesParser.TryParse(sql);
        if (!valuesResult.Success)
        {
            diagnostics.Add(new DiagnosticInfo(
                DescriptorId: "ZAO071",
                Location: methodLocation,
                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                    method.Name,
                    valuesResult.TupleCount.ToString(global::System.Globalization.CultureInfo.InvariantCulture)))));
            return EmitShape.Unknown;
        }

        // 3. Per-placeholder TRow property resolution. Case-insensitive name
        //    match against public instance properties (records / classes both
        //    expose ctor params as auto-properties at the same case as the
        //    parameter name). One ZAO072 per unresolved placeholder so adopters
        //    see every typo in a single pass — matches the FlatRow / DomainEntity
        //    column-binding diagnostic pattern.
        var rowTypeNamed = rowType.WithNullableAnnotation(NullableAnnotation.NotAnnotated) as INamedTypeSymbol;
        var rowProperties = new global::System.Collections.Generic.Dictionary<string, IPropertySymbol>(
            global::System.StringComparer.OrdinalIgnoreCase);
        if (rowTypeNamed is not null)
        {
            foreach (var member in rowTypeNamed.GetMembers())
            {
                if (member is IPropertySymbol prop
                    && prop.DeclaredAccessibility == Accessibility.Public
                    && !prop.IsStatic
                    && prop.GetMethod is not null
                    && !rowProperties.ContainsKey(prop.Name))
                {
                    rowProperties.Add(prop.Name, prop);
                }
            }
        }

        var rowTypeDisplay = rowType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var rowTypeShortDisplay = rowType.ToDisplayString();
        var anyUnresolved = false;
        var resolvedBindings = new global::System.Collections.Generic.List<BulkInsertPlaceholderBinding>(valuesResult.Placeholders.Count);
        foreach (var placeholder in valuesResult.Placeholders)
        {
            if (!rowProperties.TryGetValue(placeholder, out var matchedProp))
            {
                diagnostics.Add(new DiagnosticInfo(
                    DescriptorId: "ZAO072",
                    Location: methodLocation,
                    MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                        method.Name,
                        rowTypeShortDisplay,
                        placeholder))));
                anyUnresolved = true;
                continue;
            }

            // Build the convention info using the same ConventionDiscovery path
            // the single-row [Command] parameter binding uses. Primitive props
            // produce a null ConventionInfo; VOs / single-arg ctors / static
            // factories produce a populated one; enums (default and
            // [StoreAsString]) produce a cast/ToString convention. The Task 6
            // emit branches on Convention.Kind exactly like the existing
            // per-parameter binding helper.
            var underlying = UnwrapNullableValueType(matchedProp.Type);
            var resolution = ConventionDiscovery.Resolve(underlying, conventionContext);
            var underlyingReader = resolution.Kind switch
            {
                ConventionKind.ValueObject or ConventionKind.SingleArgCtor or ConventionKind.StaticFactory
                    => ResolveUnderlyingReaderForFactory(resolution),
                ConventionKind.Enum => ResolveUnderlyingReaderForEnum(underlying),
                ConventionKind.EnumAsString => "GetString",
                _ => null,
            };
            var convention = BuildConventionInfo(underlying, resolution, underlyingReader);

            resolvedBindings.Add(new BulkInsertPlaceholderBinding(
                PlaceholderName: placeholder,
                PropertyName: matchedProp.Name,
                Convention: convention));
        }
        if (anyUnresolved)
        {
            return EmitShape.Unknown;
        }

        // 4. Return type validation. Accept Task<int> (rows-affected sum) and
        //    Task<IReadOnlyList<TIdentity>> where TIdentity ∈ {int, long, Guid}
        //    or a VO/factory wrapping one of those. ValueTask<...> variants are
        //    intentionally NOT in scope for v1.3 — the chunked emit awaits N
        //    times and the Task<T> shape composes cleanly; adding ValueTask is
        //    a future symmetry win, not a v1.3 requirement.
        var returnKind = BulkInsertReturnKind.RowsAffected;
        string? identityTypeFullName = null;
        string? identityReaderMethod = null;
        string? identityFactory = null;
        var returnAccepted = false;

        if (method.ReturnType is INamedTypeSymbol returnNamed
            && returnNamed.Arity == 1
            && string.Equals(returnNamed.Name, "Task", StringComparison.Ordinal)
            && returnNamed.TypeArguments.Length == 1)
        {
            var taskInner = returnNamed.TypeArguments[0];

            // Task<int> — straightforward rows-affected return.
            if (taskInner.SpecialType == SpecialType.System_Int32)
            {
                returnKind = BulkInsertReturnKind.RowsAffected;
                returnAccepted = true;
            }
            // Task<IReadOnlyList<TIdentity>> — identity-buffer return.
            else if (taskInner is INamedTypeSymbol listInner
                && listInner.Arity == 1
                && string.Equals(listInner.MetadataName, "IReadOnlyList`1", StringComparison.Ordinal)
                && string.Equals(
                    listInner.ContainingNamespace?.ToDisplayString(),
                    "System.Collections.Generic",
                    StringComparison.Ordinal))
            {
                var identityCandidate = listInner.TypeArguments[0]
                    .WithNullableAnnotation(NullableAnnotation.NotAnnotated);

                // Primitive identity (int / long / Guid). No factory; the emit
                // reads the column directly via reader.GetXxx(0).
                if (IsIdentityPrimitive(identityCandidate))
                {
                    identityReaderMethod = PrimitiveCatalog.GetScalarReaderMethod(identityCandidate);
                    if (identityReaderMethod is not null)
                    {
                        identityTypeFullName = identityCandidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        identityFactory = null;
                        returnKind = BulkInsertReturnKind.IdentityList;
                        returnAccepted = true;
                    }
                }
                else
                {
                    // VO / SingleArgCtor / StaticFactory wrapping one of the
                    // identity primitives. ResolveIdentityUnderlyingReaderForFactory
                    // applies the same int/long/Guid filter on the underlying
                    // type so e.g. `record OrderId(decimal Value)` is rejected.
                    var identityResolution = ConventionDiscovery.Resolve(identityCandidate, conventionContext);
                    if (identityResolution.Kind == ConventionKind.ValueObject
                        || identityResolution.Kind == ConventionKind.SingleArgCtor
                        || identityResolution.Kind == ConventionKind.StaticFactory)
                    {
                        identityReaderMethod = ResolveIdentityUnderlyingReaderForFactory(identityResolution);
                        if (identityReaderMethod is not null)
                        {
                            identityTypeFullName = identityCandidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            // Emit will render `new global::Ns.OrderId(reader.GetXxx(0))`
                            // for SingleArgCtor / ValueObject ctors, and
                            // `global::Ns.OrderId.From(reader.GetXxx(0))` for static
                            // factories. We capture the bare ctor reference here;
                            // the emit prepends `new ` (ctor) or appends `.Name`
                            // (factory) via Convention-like resolution. For v1.3
                            // simplicity, we record the ctor-style reference for
                            // all three; emit handles the dispatch via the resolution
                            // it re-runs at codegen time. The IdentityFactory string
                            // is used purely as a marker that a factory wrap is
                            // needed — the emit consults Convention metadata on
                            // its own. Future task: thread the full ConventionInfo
                            // here so emit doesn't re-resolve. For now, capture
                            // the ctor-style string so the Task 6 emit has a
                            // non-null sentinel and a printable type name.
                            identityFactory = "new " + identityTypeFullName;
                            returnKind = BulkInsertReturnKind.IdentityList;
                            returnAccepted = true;
                        }
                    }
                }
            }
        }

        if (!returnAccepted)
        {
            diagnostics.Add(new DiagnosticInfo(
                DescriptorId: "ZAO073",
                Location: methodLocation,
                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                    method.Name,
                    method.ReturnType.ToDisplayString()))));
            return EmitShape.Unknown;
        }

        // 5. Compute chunk size: 900 / placeholderCount, floor of 1. The 900
        //    constant stays below SQL Server's 2100-parameter ceiling with
        //    headroom for the static parameters that surround the VALUES tuple.
        //    Other providers (Postgres, Sqlite, MySQL) have looser limits so
        //    900 is a portable upper bound. Integer division here matches the
        //    constant the runtime SQL-builder expects.
        var placeholderCount = resolvedBindings.Count;
        var chunkSize = placeholderCount > 0 ? 900 / placeholderCount : 1;
        if (chunkSize < 1) chunkSize = 1;

        // 6. Splice the static head / tail around the VALUES tuple. The parser
        //    reports TupleStart / TupleLength on the literal `(...)` portion,
        //    so head = sql[..TupleStart] and tail = sql[TupleStart+TupleLength..].
        //    No trimming — the head needs to keep its trailing space before
        //    `(` and the tail needs to keep its leading space before any
        //    `RETURNING` clause. Task 6's emit splices `(...,...,...)` between
        //    them once per row in the chunk.
        var insertStaticHead = sql.Substring(0, valuesResult.TupleStart);
        var insertStaticTail = sql.Substring(valuesResult.TupleStart + valuesResult.TupleLength);

        materialization = new BulkInsertMaterializationModel(
            PlaceholderBindings: new EquatableArray<BulkInsertPlaceholderBinding>(
                resolvedBindings.ToImmutableArray()),
            ChunkSize: chunkSize,
            ReturnKind: returnKind,
            IdentityTypeFullName: identityTypeFullName,
            IdentityReaderMethod: identityReaderMethod,
            IdentityFactory: identityFactory,
            RowTypeFullName: rowTypeDisplay,
            CollectionParameterName: collectionParameter.Name,
            CollectionParameterIsReadOnlyList: isReadOnlyList,
            InsertStaticHead: insertStaticHead,
            InsertStaticTail: insertStaticTail);
        return EmitShape.BulkInsertCommand;
    }

    // Shared classification path for the two scalar-like [Command] shapes
    // (Scalar in Phase B, Identity in Phase C). Both reduce to a single
    // ExecuteScalarAsync read whose result becomes the method's return value;
    // they differ only in (a) whether `T?` is accepted on the return and
    // (b) which ConventionDiscovery resolutions are honored.
    //
    // `resolveReader` returns the IDataReader.GetXxx method name to use for the
    // unwrapped type, or null to reject this kind+type combination (caller
    // falls through to Unknown and ZAO002 fires upstream).
    //
    // Critical: the MaterializationModel shape produced here is consumed
    // byte-identically by EmitCommandScalar / EmitCommandIdentity — any change
    // to the binding fields or ordering will surface as a snapshot drift.
    private static (EmitShape Shape, string? NullableReaderMethod, MaterializationModel? Materialization, MultiResultMaterializationModel? MultiResultMaterialization, bool HasReturnValue, SprocOutputParamsMaterializationModel? SprocOutputParams, BulkInsertMaterializationModel? BulkInsertMaterialization) ClassifyScalarLikeReturn(
        IMethodSymbol method,
        ConventionContext conventionContext,
        bool allowNullable,
        global::System.Func<ITypeSymbol, ConventionResult, string?> resolveReader,
        EmitShape shape)
    {
        if (method.ReturnType is not INamedTypeSymbol taskReturn)
            return (EmitShape.Unknown, null, null, null, HasReturnValue: false, SprocOutputParams: null, BulkInsertMaterialization: null);
        // Task<T> / ValueTask<T> with arity 1.
        if (taskReturn.Arity != 1
            || (taskReturn.Name != "Task" && taskReturn.Name != "ValueTask")
            || taskReturn.TypeArguments.Length != 1)
        {
            return (EmitShape.Unknown, null, null, null, HasReturnValue: false, SprocOutputParams: null, BulkInsertMaterialization: null);
        }

        var inner = taskReturn.TypeArguments[0];

        // Detect nullable wrapper — `T?` reference annotation or `Nullable<T>`.
        var isNullable = inner.NullableAnnotation == NullableAnnotation.Annotated
            || (inner is INamedTypeSymbol n
                && n.IsGenericType
                && n.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T);
        if (isNullable && !allowNullable)
        {
            // Identity hits this branch — fall through so ZAO002 fires upstream.
            return (EmitShape.Unknown, null, null, null, HasReturnValue: false, SprocOutputParams: null, BulkInsertMaterialization: null);
        }

        var unwrapped = UnwrapNullableValueType(inner)
            .WithNullableAnnotation(NullableAnnotation.NotAnnotated);

        // Type display carries the UNWRAPPED (non-nullable) type — the column
        // binding stores the cast-target type and tracks the nullable bit
        // separately on IsNullable. Downstream emitters (scalar / row) that
        // need `(T?)null` append the `?` based on IsNullable.
        var displayFormat = SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        var typeDisplay = unwrapped.ToDisplayString(displayFormat);

        var resolution = ConventionDiscovery.Resolve(unwrapped, conventionContext);
        string? reader = resolveReader(unwrapped, resolution);
        if (reader is null)
        {
            return (EmitShape.Unknown, null, null, null, HasReturnValue: false, SprocOutputParams: null, BulkInsertMaterialization: null);
        }

        var convention = BuildConventionInfo(unwrapped, resolution, reader);

        // The MaterializationModel carries one ColumnBinding describing the
        // scalar's type + factory wiring; the per-shape emitter reads it to
        // build the cast / factory expression. TargetTypeFullName carries the
        // unwrapped (non-nullable) type's fully-qualified name — the emit
        // appends the nullable annotation independently when needed.
        var binding = new ColumnBinding(
            GetterMethod: reader,
            IsNullable: isNullable,
            TypeName: typeDisplay,
            Convention: convention);
        var materialization = new MaterializationModel(
            Kind: MaterializationKind.ScalarPrimitive,
            TargetTypeFullName: unwrapped.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Columns: new EquatableArray<ColumnBinding>(ImmutableArray.Create(binding)));

        return (shape, null, materialization, null, HasReturnValue: true, SprocOutputParams: null, BulkInsertMaterialization: null);
    }

    // Identity-key primitives. The standard auto-increment / RETURNING shape
    // surfaces as int (most providers, narrow tables), long (bigint columns),
    // or Guid (uuid columns / NEWID/NEWSEQUENTIALID on SQL Server). Other
    // primitives — decimal Id, string Id, etc. — are technically possible
    // but rare enough that we steer adopters toward Kind=Scalar where the
    // shape isn't surprising.
    private static bool IsIdentityPrimitive(ITypeSymbol type)
        => type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64
           || (type.MetadataName == "Guid"
               && type.ContainingNamespace?.Name == "System"
               && type.ContainingNamespace?.ContainingNamespace?.IsGlobalNamespace == true);

    // VO / SingleArgCtor / StaticFactory variants: require the wrapped /
    // factory-arg type to be one of the identity primitives (int / long / Guid).
    // Returns the underlying reader method or null if the wrapped type is
    // outside the identity set. Mirrors ResolveUnderlyingReaderForFactory's
    // factory-parameter inspection but layered with the identity-primitive
    // filter so a `record OrderId(decimal Value)` would be rejected.
    private static string? ResolveIdentityUnderlyingReaderForFactory(ConventionResult resolution)
    {
        ITypeSymbol? underlying = resolution.Factory switch
        {
            IMethodSymbol m when m.Parameters.Length == 1 => m.Parameters[0].Type,
            _ => null,
        };
        if (underlying is null || !IsIdentityPrimitive(underlying))
            return null;
        return PrimitiveCatalog.GetScalarReaderMethod(underlying);
    }

    // Attempt to classify `elementType` as a positional record whose constructor params
    // resolve to known conventions. Returns null if the type isn't a record, has no
    // public ctor with parameters, or any parameter falls outside the conventions
    // currently emittable (primitive scalars + Phase-C value-object shapes).
    //
    // Each column's primitive/value-object/factory disposition is captured via
    // ConventionDiscovery.Resolve and projected onto a cache-safe ConventionInfo. The
    // primitive path stays byte-identical to v0.1 (ColumnBinding.Convention is null);
    // non-primitive resolutions carry ConventionInfo for the emitter to consume.
    private static MaterializationModel? TryBuildFlatRowMaterialization(
        ITypeSymbol elementType,
        ConventionContext conventionContext,
        ImmutableArray<DiagnosticInfo>.Builder? diagnostics = null,
        string? methodName = null,
        LocationInfo? location = null)
    {
        if (elementType is not INamedTypeSymbol named || !named.IsRecord) return null;

        // Pick the widest public instance ctor — positional records synthesize one
        // ctor matching the parameter list. Multiple public ctors stay supported by
        // taking the widest, mirroring how Dapper resolves materialization ctors.
        var ctor = named.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length > 0)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();
        if (ctor is null) return null;

        var columns = ImmutableArray.CreateBuilder<ColumnBinding>(ctor.Parameters.Length);

        foreach (var p in ctor.Parameters)
        {
            // For nullable value types (`int?`) we strongly-type-read the underlying
            // primitive. For nullable reference types (`string?`) the reference type
            // IS the reader-target — string is read with GetString regardless of nullability.
            var underlying = UnwrapNullableValueType(p.Type);
            var resolution = ConventionDiscovery.Resolve(underlying, conventionContext);

            // v0.5 Phase A — composite ctor parameter (Money(decimal, string)
            // embedded in `record OrderRow(int Id, Money Total)`). The inner columns
            // flatten into the outer FlatRow's column list at the emit level; here
            // we capture them as InnerColumns on the binding so the column-index
            // math threads through correctly.
            //
            // v0.5 Phase C — nullable composite ctor params (`Money? Total`) are
            // now accepted; TryBuildCompositeColumnBinding propagates the
            // IsNullable flag and the FlatRow emit recognises it as "switch to
            // hoisted-local + all-or-nothing branching".
            // v0.5 Phase D — `[Materialize(Factory = "X")]` on the inner type wins
            // over the MultiArgCtor convention. Inner factory dispatch attached
            // to the ColumnBinding's FactoryMethodName field; nested-composite
            // construction in EmitFlatRow swaps the `new T(...)` for the
            // factory call. ZAO043 fires when the named static method is missing.
            var innerFactoryName = ReadMaterializeFactoryName(underlying.GetAttributes());
            if (innerFactoryName is not null)
            {
                if (diagnostics is not null && methodName is not null)
                {
                    var factoryBinding = TryBuildFactoryDispatchColumnBinding(
                        p, underlying, innerFactoryName, conventionContext,
                        useNamedColumns: false,
                        diagnostics: diagnostics,
                        methodName: methodName,
                        location: location);
                    if (factoryBinding is null) return null;
                    columns.Add(factoryBinding);
                    continue;
                }
                // No diagnostics sink (streaming / multi-result paths). Try the
                // factory silently; bail to ZAO040 / ZAO022 if it doesn't resolve.
                if (underlying is INamedTypeSymbol inn
                    && LookupStaticFactory(inn, innerFactoryName) is IMethodSymbol fac
                    && TryBuildFactoryDispatchInnerColumns(fac, conventionContext, useNamedColumns: false) is { } innerCols)
                {
                    var compositeTypeDisplay = underlying
                        .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                        .ToDisplayString(TypeDisplayFormat);
                    var isCompositeNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated
                        || (p.Type is INamedTypeSymbol cp
                            && cp.IsGenericType
                            && cp.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T);
                    columns.Add(new ColumnBinding(
                        GetterMethod: string.Empty,
                        IsNullable: isCompositeNullable,
                        TypeName: compositeTypeDisplay,
                        Convention: null,
                        ColumnName: null,
                        InnerColumns: new EquatableArray<ColumnBinding>(innerCols),
                        CtorArgName: p.Name,
                        FactoryMethodName: innerFactoryName));
                    continue;
                }
                return null;
            }

            if (resolution.Kind == ConventionKind.MultiArgCtor)
            {
                var compBinding = TryBuildCompositeColumnBinding(p, underlying, resolution, conventionContext, useNamedColumns: false);
                if (compBinding is null) return null; // inner-column resolution failure.
                columns.Add(compBinding);
                continue;
            }

            // v0.5 Phase E.1 — ZAO052: the ctor param type didn't resolve to a
            // supported convention. Before falling through to the generic
            // reader-null bail (which surfaces as ZAO022 / ZAO040 upstream),
            // probe for the recursive-composite shape so adopters get the
            // specific "composite-of-composite deferred to v0.6+" diagnostic.
            if (diagnostics is not null
                && methodName is not null
                && resolution.Kind == ConventionKind.Unknown
                && TryDetectRecursiveCompositeInner(underlying, conventionContext) is { } recursive)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DescriptorId: "ZAO052",
                    Location: location,
                    MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                        methodName,
                        recursive.OuterTypeDisplay,
                        recursive.InnerCtorArgName))));
                return null;
            }

            // Pull the reader-method that lets the emitter read the wrapped primitive.
            // For Primitive conventions this IS the read; for ValueObject /
            // SingleArgCtor / StaticFactory we read the underlying primitive then
            // pass it to the factory.
            string? reader = resolution.Kind switch
            {
                ConventionKind.Primitive => PrimitiveCatalog.GetScalarReaderMethod(underlying),
                ConventionKind.ValueObject or ConventionKind.SingleArgCtor or ConventionKind.StaticFactory
                    => ResolveUnderlyingReaderForFactory(resolution),
                // Enum (default int round-trip) reads via the enum's underlying integral
                // primitive — typically GetInt32; byte/short/long-backed enums route
                // through their matching reader (GetByte / GetInt16 / GetInt64). The
                // emitter then wraps the read in a `(EnumType)` cast.
                ConventionKind.Enum => ResolveUnderlyingReaderForEnum(underlying),
                // [StoreAsString] enums round-trip as strings; reader is GetString
                // and the emitter wraps the read in `global::System.Enum.Parse<T>(...)`.
                ConventionKind.EnumAsString => "GetString",
                _ => null,
            };
            if (reader is null) return null;

            var isNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated
                || (p.Type is INamedTypeSymbol pn
                    && pn.IsGenericType
                    && pn.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T);

            var convention = BuildConventionInfo(underlying, resolution, reader);

            // TypeName stores the UNWRAPPED type — IsNullable carries the nullable
            // bit. Downstream emitters that need `(T?)null` append the `?` based
            // on IsNullable. Strip both `Nullable<T>` (already done by `underlying`)
            // AND the reference-type nullable annotation so `string?` -> `string`.
            var unwrappedDisplay = underlying
                .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                .ToDisplayString(TypeDisplayFormat);

            columns.Add(new ColumnBinding(
                GetterMethod: reader,
                IsNullable: isNullable,
                TypeName: unwrappedDisplay,
                Convention: convention));
        }

        return new MaterializationModel(
            Kind: MaterializationKind.FlatRow,
            TargetTypeFullName: named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Columns: new EquatableArray<ColumnBinding>(columns.MoveToImmutable()));
    }

    // v0.5 Phase A — composite at scalar position. Build a MaterializationModel
    // whose Columns list flattens the composite's ctor parameters into N inner
    // ColumnBindings. The model's TargetTypeFullName carries the composite type
    // itself (Money) — the emit calls `new TargetTypeFullName(__reader.GetXxx(0), ...)`.
    //
    // Returns null when the type isn't classifiable as a composite via
    // ConventionDiscovery.MultiArgCtor (e.g. it's a primitive, an enum, a
    // single-arg-ctor record, or recursive — recursive bails out inside
    // ConventionDiscovery itself).
    private static MaterializationModel? TryBuildCompositeMaterialization(
        ITypeSymbol elementType,
        ConventionContext conventionContext)
    {
        if (elementType is not INamedTypeSymbol named) return null;
        var resolution = ConventionDiscovery.Resolve(named, conventionContext);
        if (resolution.Kind != ConventionKind.MultiArgCtor) return null;

        var columns = TryBuildCompositeInnerColumns(resolution, conventionContext);
        if (columns is null) return null;

        return new MaterializationModel(
            Kind: MaterializationKind.Composite,
            TargetTypeFullName: named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Columns: new EquatableArray<ColumnBinding>(columns.Value));
    }

    // v0.5 Phase E.1 — probe for the "recursive composite" shape that
    // TryMultiArgCtor rejects (returns Unknown) instead of accepting:
    // a candidate type that has exactly one public ctor with > 1 parameters
    // where at least one ctor parameter type itself resolves to MultiArgCtor.
    // Pre-Phase-E this silently fell through to ZAO022; ZAO052 fires here
    // with a specific message + workaround hint.
    //
    // Returns the name of the FIRST recursive inner ctor parameter when
    // detected; null otherwise. Skips types that carry [Materialize(Factory)]
    // (the factory dispatch path bypasses MultiArgCtor and is the documented
    // workaround). Skips types where the inner composite carries its own
    // [Materialize(Factory)] — adopters using the documented factory escape
    // hatch should not see ZAO052.
    private static (string OuterTypeDisplay, string InnerCtorArgName)? TryDetectRecursiveCompositeInner(
        ITypeSymbol elementType,
        ConventionContext conventionContext)
    {
        if (elementType is not INamedTypeSymbol named) return null;
        // Skip if the outer type opts into [Materialize(Factory)] — that
        // dispatch wins over MultiArgCtor convention (discovery order rule #1).
        if (ReadMaterializeFactoryName(named.GetAttributes()) is not null) return null;

        // Mirror TryMultiArgCtor's ctor-selection rule: single public N-arg
        // (N > 1) ctor, no ambiguity.
        IMethodSymbol? ctor = null;
        var matchCount = 0;
        foreach (var c in named.InstanceConstructors)
        {
            if (c.DeclaredAccessibility != Accessibility.Public || c.Parameters.Length <= 1) continue;
            matchCount++;
            if (matchCount > 1) return null;
            ctor = c;
        }
        if (ctor is null) return null;

        foreach (var p in ctor.Parameters)
        {
            var underlying = UnwrapNullableValueType(p.Type);
            // Inner with explicit [Materialize(Factory)] is the documented
            // workaround — don't fire ZAO052 in that case even if the
            // factory's underlying composite shape looks recursive.
            if (ReadMaterializeFactoryName(underlying.GetAttributes()) is not null) continue;
            var innerResolution = ConventionDiscovery.Resolve(underlying, conventionContext);
            if (innerResolution.Kind == ConventionKind.MultiArgCtor)
            {
                var outerDisplay = named
                    .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                    .ToDisplayString(TypeDisplayFormat);
                return (outerDisplay, p.Name);
            }
        }
        return null;
    }

    // Build the inner-column ColumnBinding list for a composite type. Each ctor
    // parameter resolves to a single column (primitive / VO / SingleArgCtor /
    // StaticFactory / Enum / EnumAsString); recursive composites are rejected at
    // the ConventionDiscovery layer so the list is one level deep.
    private static ImmutableArray<ColumnBinding>? TryBuildCompositeInnerColumns(
        ConventionResult resolution,
        ConventionContext conventionContext)
    {
        var ctorParams = resolution.ExpandedColumns;
        // Defensive: TryMultiArgCtor upstream already enforces Parameters.Length > 1,
        // but this guard protects against future ConventionDiscovery contract drift.
        if (ctorParams.IsDefault || ctorParams.Length < 2) return null;

        var inner = ImmutableArray.CreateBuilder<ColumnBinding>(ctorParams.Length);
        foreach (var p in ctorParams)
        {
            var underlying = UnwrapNullableValueType(p.Type);
            var innerResolution = ConventionDiscovery.Resolve(underlying, conventionContext);

            string? reader = innerResolution.Kind switch
            {
                ConventionKind.Primitive => PrimitiveCatalog.GetScalarReaderMethod(underlying),
                ConventionKind.ValueObject or ConventionKind.SingleArgCtor or ConventionKind.StaticFactory
                    => ResolveUnderlyingReaderForFactory(innerResolution),
                ConventionKind.Enum => ResolveUnderlyingReaderForEnum(underlying),
                ConventionKind.EnumAsString => "GetString",
                _ => null,
            };
            if (reader is null) return null;

            var isNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated
                || (p.Type is INamedTypeSymbol pn
                    && pn.IsGenericType
                    && pn.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T);

            var convention = BuildConventionInfo(underlying, innerResolution, reader);
            var unwrappedDisplay = underlying
                .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                .ToDisplayString(TypeDisplayFormat);

            inner.Add(new ColumnBinding(
                GetterMethod: reader,
                IsNullable: isNullable,
                TypeName: unwrappedDisplay,
                Convention: convention,
                // Post-review Fix 2 — capture the inner ctor-arg name so the
                // mixed-null exception message + hoisted-local naming have a
                // human-readable fallback when ColumnName is null (FlatRow
                // positional path).
                CtorArgName: p.Name));
        }
        return inner.MoveToImmutable();
    }

    // v0.5 Phase A (post-review Fix 6) — shared helper for "composite ctor param
    // nested inside a FlatRow / DomainEntity row". Builds the inner-column list,
    // optionally populates ColumnName for DomainEntity callers, and constructs
    // the outer ColumnBinding. Returns null only when the inner-column
    // resolution fails.
    //
    // v0.5 Phase C — nullable composite ctor params (`Money? Total`) are now in
    // scope. The outer ColumnBinding carries IsNullable: true when the C# type
    // is annotated nullable or `Nullable<T>`. The downstream emitter
    // (EmitFlatRow / EmitDomainEntity) recognises the flag and emits the
    // hoisted-local + all-or-nothing pattern instead of inlining the
    // composite construction into the outer `new T(...)` argument list.
    // ZAO050 fires at the diagnostic layer for every nullable composite
    // position so adopters see the "all-or-nothing runtime check" warning.
    //
    // `useNamedColumns` toggles the inner reads between positional (FlatRow,
    // ColumnName: null) and column-name-keyed (DomainEntity, ColumnName: PascalCase
    // of each inner ctor param name).
    private static ColumnBinding? TryBuildCompositeColumnBinding(
        IParameterSymbol param,
        ITypeSymbol underlying,
        ConventionResult resolution,
        ConventionContext conventionContext,
        bool useNamedColumns)
    {
        var isCompositeNullable = param.Type.NullableAnnotation == NullableAnnotation.Annotated
            || (param.Type is INamedTypeSymbol cp
                && cp.IsGenericType
                && cp.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T);

        var innerCols = TryBuildCompositeInnerColumns(resolution, conventionContext);
        if (innerCols is null) return null;

        var innerBindings = innerCols.Value;
        if (useNamedColumns)
        {
            // For DomainEntity the inner reads use column-name lookups; populate
            // ColumnName on each inner binding from the ctor-param name.
            var ctorParams = resolution.ExpandedColumns;
            var withNames = ImmutableArray.CreateBuilder<ColumnBinding>(innerBindings.Length);
            for (var ix = 0; ix < innerBindings.Length; ix++)
            {
                var b = innerBindings[ix];
                withNames.Add(b with { ColumnName = ToPascalCase(ctorParams[ix].Name) });
            }
            innerBindings = withNames.MoveToImmutable();
        }

        var compositeTypeDisplay = underlying
            .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(TypeDisplayFormat);
        return new ColumnBinding(
            GetterMethod: string.Empty, // not used for composite; reads live on InnerColumns.
            IsNullable: isCompositeNullable,
            TypeName: compositeTypeDisplay,
            Convention: null,
            ColumnName: null,
            InnerColumns: new EquatableArray<ColumnBinding>(innerBindings),
            // Post-review Fix 6 — the outer ctor-arg name (e.g. "Total" on
            // `record OrderRow(int Id, Money? Total)`) drives the readable
            // hoisted-local name for nullable composites. Falls back to
            // `__composite_<i>` when absent (defensive — `param` is never
            // null here since this helper is only called from the per-ctor-
            // parameter loops).
            CtorArgName: param.Name);
    }

    // v0.5 Phase D — build the inner-column list from a STATIC FACTORY method's
    // parameter list. Identical structurally to TryBuildCompositeInnerColumns
    // (one ColumnBinding per parameter, each resolved via ConventionDiscovery)
    // but the source is `factory.Parameters` rather than `ctor.Parameters`. The
    // factory drives column types regardless of the target type's underlying ctor
    // shape — that's the whole point of the factory pattern (Sqlite decimal-as-
    // text: `Money` ctor takes `decimal`, but `Money.FromStorage` takes `string`).
    //
    // `useNamedColumns: true` populates ColumnName for the DomainEntity nested
    // path; false leaves ColumnName null for FlatRow positional reads.
    //
    // v0.5 Phase D post-review Fix 1 — when `candidateColumnNames` is non-null,
    // factory parameter NAMES drive matching: each factory parameter must match
    // a candidate name with `OrdinalIgnoreCase`. Mismatches emit ZAO051 (when a
    // diagnostics sink is supplied) and bail. The matched candidate name (NOT
    // the PascalCased factory param name) becomes the ColumnName so the emit
    // reads via `GetOrdinal("Amount")` regardless of whether the factory
    // parameter is `amount`, `Amount`, or `amountText` (after rename).
    //
    // When `candidateColumnNames` is null we fall back to positional matching
    // (FlatRow path / composite-at-scalar — no candidate names are statically
    // available at classification time). In the positional fallback ColumnName
    // is null when useNamedColumns is false, and falls back to the PascalCased
    // factory parameter name when useNamedColumns is true.
    private static ImmutableArray<ColumnBinding>? TryBuildFactoryDispatchInnerColumns(
        IMethodSymbol factory,
        ConventionContext conventionContext,
        bool useNamedColumns,
        ImmutableArray<string>? candidateColumnNames = null,
        ImmutableArray<DiagnosticInfo>.Builder? diagnostics = null,
        string? methodName = null,
        string? factoryName = null,
        LocationInfo? location = null)
    {
        // Post-review Fix 10 — the `Parameters.Length == 0` guard was dead code:
        // LookupStaticFactory already filters parameterless factories. Removed.
        var inner = ImmutableArray.CreateBuilder<ColumnBinding>(factory.Parameters.Length);
        var useNameMatching = candidateColumnNames is { } cn && cn.Length > 0;
        foreach (var p in factory.Parameters)
        {
            var underlying = UnwrapNullableValueType(p.Type);
            var innerResolution = ConventionDiscovery.Resolve(underlying, conventionContext);

            string? reader = innerResolution.Kind switch
            {
                ConventionKind.Primitive => PrimitiveCatalog.GetScalarReaderMethod(underlying),
                ConventionKind.ValueObject or ConventionKind.SingleArgCtor or ConventionKind.StaticFactory
                    => ResolveUnderlyingReaderForFactory(innerResolution),
                ConventionKind.Enum => ResolveUnderlyingReaderForEnum(underlying),
                ConventionKind.EnumAsString => "GetString",
                _ => null,
            };
            if (reader is null)
            {
                if (diagnostics is not null && methodName is not null && factoryName is not null)
                {
                    EmitZAO043(
                        diagnostics, methodName, factoryName,
                        factory.ContainingType.ToDisplayString(),
                        $"factory parameter type '{underlying.ToDisplayString()}' could not be resolved by ConventionDiscovery",
                        location);
                }
                return null;
            }

            var isNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated
                || (p.Type is INamedTypeSymbol pn
                    && pn.IsGenericType
                    && pn.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T);

            var convention = BuildConventionInfo(underlying, innerResolution, reader);
            var unwrappedDisplay = underlying
                .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                .ToDisplayString(TypeDisplayFormat);

            // Post-review Fix 1 — resolve the column NAME for this factory parameter.
            // When candidate names are available (nested-in-composite path where the
            // underlying type's ctor parameter names provide the candidate set), do
            // case-insensitive name matching and bail with ZAO051 on mismatch.
            // Otherwise (FlatRow positional / composite-at-scalar path) fall back to
            // positional + PascalCased factory parameter name.
            string? columnName;
            if (useNameMatching)
            {
                string? matched = null;
                foreach (var candidate in candidateColumnNames!.Value)
                {
                    if (string.Equals(candidate, p.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = candidate;
                        break;
                    }
                }
                if (matched is null)
                {
                    if (diagnostics is not null && methodName is not null && factoryName is not null)
                    {
                        EmitZAO051(
                            diagnostics, methodName, factoryName, p.Name,
                            candidateColumnNames.Value, location);
                    }
                    return null;
                }
                columnName = useNamedColumns ? matched : null;
            }
            else
            {
                columnName = useNamedColumns ? ToPascalCase(p.Name) : null;
            }

            inner.Add(new ColumnBinding(
                GetterMethod: reader,
                IsNullable: isNullable,
                TypeName: unwrappedDisplay,
                Convention: convention,
                ColumnName: columnName,
                CtorArgName: p.Name));
        }
        return inner.MoveToImmutable();
    }

    // Post-review Fix 1 — extract the candidate column-name set for name-based
    // factory parameter matching. The candidate set comes from the underlying
    // composite type's MultiArgCtor parameter names (PascalCased), which is what
    // the convention discovery path would use as column names. Returns null
    // when the type has no resolvable MultiArgCtor — in that case positional
    // matching is used as the documented fallback (ZAO051 doesn't fire).
    private static ImmutableArray<string>? TryGetFactoryColumnNameCandidates(
        ITypeSymbol underlying,
        ConventionContext conventionContext)
    {
        if (underlying is not INamedTypeSymbol named) return null;
        var resolution = ConventionDiscovery.Resolve(named, conventionContext);
        if (resolution.Kind != ConventionKind.MultiArgCtor) return null;
        var ctorParams = resolution.ExpandedColumns;
        if (ctorParams.IsDefault || ctorParams.Length == 0) return null;
        var builder = ImmutableArray.CreateBuilder<string>(ctorParams.Length);
        foreach (var p in ctorParams)
        {
            builder.Add(ToPascalCase(p.Name));
        }
        return builder.MoveToImmutable();
    }

    // Post-review Fix 6 — extracted from ClassifyEmitShape. Resolve the Phase D
    // factory branch at scalar return position. Returns:
    //   * non-null tuple => the caller MUST return it from the classifier
    //     (either a successful Composite shape, or Unknown after ZAO043/044/051
    //     fired).
    //   * null => no [Materialize(Factory)] applies; the classifier should
    //     continue with composite / FlatRow / DomainEntity branches.
    private static (EmitShape Shape, string? NullableReaderMethod, MaterializationModel? Materialization, MultiResultMaterializationModel? MultiResultMaterialization, bool HasReturnValue, SprocOutputParamsMaterializationModel? SprocOutputParams, BulkInsertMaterializationModel? BulkInsertMaterialization)? ResolveScalarFactoryShape(
        IMethodSymbol method,
        ITypeSymbol inner,
        ConventionContext conventionContext,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        LocationInfo? returnTypeLocation)
    {
        var factoryInnerCandidate = UnwrapNullableValueType(inner)
            .WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        var factoryName = ResolveReturnTypeFactoryName(method, factoryInnerCandidate);

        // Post-review Fix 8 — [Materialize(Strategy = Custom)] without Factory
        // is a silent no-op today (the classifier would otherwise fall through
        // to convention discovery). Surface a tailored ZAO043 message so the
        // adopter sees the misconfiguration at compile time.
        if (factoryName is null)
        {
            var methodLevelHasCustomWithoutFactory =
                HasCustomStrategyWithoutFactory(method.GetReturnTypeAttributes());
            var typeLevelHasCustomWithoutFactory =
                HasCustomStrategyWithoutFactory(factoryInnerCandidate.GetAttributes());
            if (methodLevelHasCustomWithoutFactory || typeLevelHasCustomWithoutFactory)
            {
                EmitZAO043(
                    diagnostics, method.Name,
                    factoryName: "<missing>",
                    targetTypeDisplay: factoryInnerCandidate.ToDisplayString(),
                    reason: "[Materialize(Strategy = Custom)] requires a Factory argument",
                    location: returnTypeLocation);
                return (EmitShape.Unknown, null, null, null, HasReturnValue: false, SprocOutputParams: null, BulkInsertMaterialization: null);
            }
            return null;
        }

        var factoryIsNullableValueType =
            inner is INamedTypeSymbol fcn
            && fcn.IsGenericType
            && fcn.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T;
        var factoryIsNullableRef =
            inner.IsReferenceType
            && inner.NullableAnnotation == NullableAnnotation.Annotated;
        var factoryMat = TryBuildFactoryDispatchMaterialization(
            factoryInnerCandidate,
            factoryName,
            isNullable: factoryIsNullableValueType || factoryIsNullableRef,
            conventionContext: conventionContext,
            diagnostics: diagnostics,
            methodName: method.Name,
            location: returnTypeLocation);
        if (factoryMat is not null)
        {
            return (EmitShape.Composite, null, factoryMat, null, HasReturnValue: true, SprocOutputParams: null, BulkInsertMaterialization: null);
        }
        // factoryMat == null after ZAO043/044/051 fired upstream — fall through
        // to Unknown so the partial-method stub doesn't emit. ZAO022/040 are
        // suppressed by the diagnostic-builder scan in TransformMethod (Fix 4).
        return (EmitShape.Unknown, null, null, null, HasReturnValue: false, SprocOutputParams: null, BulkInsertMaterialization: null);
    }

    // v0.5 Phase D — build a Composite-kind MaterializationModel that invokes a
    // static factory in place of the type's ctor. ZAO043 fires (via the supplied
    // diagnostics builder) when the named factory is missing OR isn't static OR
    // any factory parameter type fails convention discovery.
    //
    // The factory's parameter list drives the inner-column shape — see
    // TryBuildFactoryDispatchInnerColumns for the per-parameter ConventionDiscovery
    // unwrap. `isNullable` flips on the all-or-nothing emit path identical to
    // Phase C nullable-composite handling.
    private static MaterializationModel? TryBuildFactoryDispatchMaterialization(
        ITypeSymbol targetType,
        string factoryName,
        bool isNullable,
        ConventionContext conventionContext,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        string methodName,
        LocationInfo? location)
    {
        if (targetType is not INamedTypeSymbol named)
        {
            EmitZAO043(diagnostics, methodName, factoryName, targetType.ToDisplayString(), "method not found", location);
            return null;
        }

        var factory = LookupStaticFactory(named, factoryName, out var overloadCount, out var failureReason);
        if (factory is null)
        {
            EmitZAO043(diagnostics, methodName, factoryName, named.ToDisplayString(), failureReason ?? "method not found", location);
            return null;
        }
        if (overloadCount > 1)
        {
            EmitZAO044_OverloadAmbiguity(diagnostics, named.ToDisplayString(), factoryName, overloadCount, location);
            return null;
        }

        // Composite-at-scalar shape: no SQL column names are available at
        // classification time, so positional matching is the documented
        // fallback. ZAO051 does not fire here.
        var inner = TryBuildFactoryDispatchInnerColumns(
            factory, conventionContext, useNamedColumns: false,
            candidateColumnNames: null,
            diagnostics: diagnostics, methodName: methodName,
            factoryName: factoryName, location: location);
        if (inner is null)
        {
            // EmitZAO043 already fired inside TryBuildFactoryDispatchInnerColumns
            // when the failure was a factory parameter type resolution miss. The
            // null return here just propagates upward.
            return null;
        }

        return new MaterializationModel(
            Kind: MaterializationKind.Composite,
            TargetTypeFullName: named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Columns: new EquatableArray<ColumnBinding>(inner.Value),
            IsNullable: isNullable,
            FactoryMethodName: factoryName);
    }

    // v0.5 Phase D — composite ctor parameter NESTED inside a FlatRow / DomainEntity
    // whose TYPE carries `[Materialize(Factory = "X")]`. Mirrors
    // TryBuildCompositeColumnBinding but the inner-column list comes from the
    // factory's parameter list (not the type's ctor) and the binding carries
    // FactoryMethodName so the emit dispatches `T.FactoryMethodName(...)` instead
    // of `new T(...)`. ZAO043 fires here too — same descriptor, same message
    // ordering — when the factory is missing / non-static / unresolvable.
    private static ColumnBinding? TryBuildFactoryDispatchColumnBinding(
        IParameterSymbol param,
        ITypeSymbol underlying,
        string factoryName,
        ConventionContext conventionContext,
        bool useNamedColumns,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        string methodName,
        LocationInfo? location)
    {
        if (underlying is not INamedTypeSymbol named)
        {
            EmitZAO043(diagnostics, methodName, factoryName, underlying.ToDisplayString(), "method not found", location);
            return null;
        }

        var factory = LookupStaticFactory(named, factoryName, out var overloadCount, out var failureReason);
        if (factory is null)
        {
            EmitZAO043(diagnostics, methodName, factoryName, named.ToDisplayString(), failureReason ?? "method not found", location);
            return null;
        }
        if (overloadCount > 1)
        {
            EmitZAO044_OverloadAmbiguity(diagnostics, named.ToDisplayString(), factoryName, overloadCount, location);
            return null;
        }

        // Post-review Fix 1 — name-based matching only applies when the inner
        // reads will be column-name-keyed (DomainEntity nested path). For the
        // FlatRow positional nested path, inner reads are positional so the
        // documented fallback (positional matching, no ZAO051) applies.
        var candidates = useNamedColumns
            ? TryGetFactoryColumnNameCandidates(named, conventionContext)
            : null;

        var inner = TryBuildFactoryDispatchInnerColumns(
            factory, conventionContext, useNamedColumns,
            candidateColumnNames: candidates,
            diagnostics: diagnostics, methodName: methodName,
            factoryName: factoryName, location: location);
        if (inner is null)
        {
            // EmitZAO043 / EmitZAO051 already fired inside
            // TryBuildFactoryDispatchInnerColumns. Propagate the null.
            return null;
        }

        var isCompositeNullable = param.Type.NullableAnnotation == NullableAnnotation.Annotated
            || (param.Type is INamedTypeSymbol cp
                && cp.IsGenericType
                && cp.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T);

        var compositeTypeDisplay = underlying
            .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(TypeDisplayFormat);
        return new ColumnBinding(
            GetterMethod: string.Empty,
            IsNullable: isCompositeNullable,
            TypeName: compositeTypeDisplay,
            Convention: null,
            ColumnName: null,
            InnerColumns: new EquatableArray<ColumnBinding>(inner.Value),
            CtorArgName: param.Name,
            FactoryMethodName: factoryName);
    }

    // Emit ZAO043 into the supplied diagnostics builder. Centralised so the
    // message-arg ordering stays consistent across the call sites (scalar-
    // position factory miss, nested-composite factory miss, factory inner-
    // column resolution miss, [Materialize(Strategy = Custom)] without
    // Factory).
    //
    // Post-review Fix 4 — the descriptor message now carries a 4th arg that
    // names the failure reason. Possible reasons:
    //   * "method not found"
    //   * "method is not static"
    //   * "method is not public"
    //   * "factory parameter type '<TypeName>' could not be resolved by ConventionDiscovery"
    //   * "[Materialize(Strategy = Custom)] requires a Factory argument"
    private static void EmitZAO043(
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        string methodName,
        string factoryName,
        string targetTypeDisplay,
        string reason,
        LocationInfo? location)
    {
        diagnostics.Add(new DiagnosticInfo(
            DescriptorId: "ZAO043",
            Location: location,
            MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                methodName,
                factoryName,
                targetTypeDisplay,
                reason))));
    }

    // Post-review Fix 2 — emit ZAO051 (factory parameter / column-name
    // mismatch). The available-columns list is rendered as a comma-separated
    // string for the message.
    private static void EmitZAO051(
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        string methodName,
        string factoryName,
        string factoryParamName,
        ImmutableArray<string> candidateColumnNames,
        LocationInfo? location)
    {
        diagnostics.Add(new DiagnosticInfo(
            DescriptorId: "ZAO051",
            Location: location,
            MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                methodName,
                factoryName,
                factoryParamName,
                string.Join(", ", candidateColumnNames)))));
    }

    // Post-review Fix 3 — emit ZAO044 for static-factory overload ambiguity.
    // Reuses the existing v0.2 descriptor (made attribute-name-agnostic in
    // Fix 3 by adding a 3rd message arg for the reason).
    private static void EmitZAO044_OverloadAmbiguity(
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        string targetTypeDisplay,
        string factoryName,
        int overloadCount,
        LocationInfo? location)
    {
        diagnostics.Add(new DiagnosticInfo(
            DescriptorId: "ZAO044",
            Location: location,
            MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                targetTypeDisplay,
                factoryName,
                $"Found {overloadCount} matching static overloads; overload selection by signature is not supported. Reduce to a single static '{factoryName}' or use a distinct factory name."))));
    }

    // v0.2 Phase E — multi-arg "class with a named-param ctor" shape. A type qualifies
    // when it's:
    //   * a class (not a record — records take the positional FlatRow path),
    //   * with exactly one public ctor that has > 0 parameters,
    //   * whose every parameter resolves to a known convention.
    // Each ctor parameter's name (PascalCased verbatim, e.g. `customerId` -> "CustomerId")
    // becomes the SQL column identifier; the emitter renders
    // `__reader.GetOrdinal("CustomerId")` so SELECT column order is irrelevant.
    private static MaterializationModel? TryBuildDomainEntityMaterialization(
        ITypeSymbol elementType,
        ConventionContext conventionContext,
        ImmutableArray<DiagnosticInfo>.Builder? diagnostics = null,
        string? methodName = null,
        LocationInfo? location = null)
    {
        if (elementType is not INamedTypeSymbol named) return null;
        // Records take the FlatRow positional path; DomainEntity is reserved for
        // plain classes. Excluding records here keeps the priority ladder explicit:
        // record class / record struct / readonly record struct -> FlatRow.
        if (named.IsRecord) return null;
        if (named.TypeKind != TypeKind.Class) return null;

        // Exactly one public ctor with > 0 parameters. If there are zero or multiple
        // public ctors with parameters we bail — the v0.2 surface is intentionally
        // strict (a class with an obvious "the" ctor). Implicit default ctors with
        // zero params don't count because they cannot bind column values.
        var publicParameterizedCtors = named.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length > 0)
            .ToImmutableArray();
        if (publicParameterizedCtors.Length != 1) return null;
        var ctor = publicParameterizedCtors[0];

        var columns = ImmutableArray.CreateBuilder<ColumnBinding>(ctor.Parameters.Length);

        foreach (var p in ctor.Parameters)
        {
            var underlying = UnwrapNullableValueType(p.Type);
            var resolution = ConventionDiscovery.Resolve(underlying, conventionContext);

            // v0.5 Phase A — composite ctor parameter on a DomainEntity. The
            // inner-column lookup uses GetOrdinal(<inner-column-name>) so each
            // primitive read is keyed by name. Composite types provide column-name
            // candidates via the inner ctor parameter names PascalCased.
            //
            // v0.5 Phase C — nullable composite ctor params on DomainEntity
            // mirror the FlatRow path; the column-name-keyed reads are reused
            // by the hoisted-local emit (EmitDomainEntity recognises the
            // IsNullable flag on the binding).
            // v0.5 Phase D — `[Materialize(Factory = "X")]` on the inner type
            // overrides MultiArgCtor convention. Same dispatch as the FlatRow path.
            var innerFactoryName = ReadMaterializeFactoryName(underlying.GetAttributes());
            if (innerFactoryName is not null)
            {
                if (diagnostics is not null && methodName is not null)
                {
                    var factoryBinding = TryBuildFactoryDispatchColumnBinding(
                        p, underlying, innerFactoryName, conventionContext,
                        useNamedColumns: true,
                        diagnostics: diagnostics,
                        methodName: methodName,
                        location: location);
                    if (factoryBinding is null) return null;
                    columns.Add(factoryBinding);
                    continue;
                }
                if (underlying is INamedTypeSymbol inn
                    && LookupStaticFactory(inn, innerFactoryName) is IMethodSymbol fac
                    && TryBuildFactoryDispatchInnerColumns(fac, conventionContext, useNamedColumns: true) is { } innerCols)
                {
                    var compositeTypeDisplay = underlying
                        .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                        .ToDisplayString(TypeDisplayFormat);
                    var isCompositeNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated
                        || (p.Type is INamedTypeSymbol cp
                            && cp.IsGenericType
                            && cp.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T);
                    columns.Add(new ColumnBinding(
                        GetterMethod: string.Empty,
                        IsNullable: isCompositeNullable,
                        TypeName: compositeTypeDisplay,
                        Convention: null,
                        ColumnName: null,
                        InnerColumns: new EquatableArray<ColumnBinding>(innerCols),
                        CtorArgName: p.Name,
                        FactoryMethodName: innerFactoryName));
                    continue;
                }
                return null;
            }

            if (resolution.Kind == ConventionKind.MultiArgCtor)
            {
                var compBinding = TryBuildCompositeColumnBinding(p, underlying, resolution, conventionContext, useNamedColumns: true);
                if (compBinding is null) return null; // inner-column resolution failure.
                columns.Add(compBinding);
                continue;
            }

            // v0.5 Phase E.1 — ZAO052: parallel to the FlatRow probe, surface
            // recursive composites at DomainEntity ctor positions with the
            // specific deferred-feature diagnostic rather than the generic
            // ZAO022 fall-through.
            if (diagnostics is not null
                && methodName is not null
                && resolution.Kind == ConventionKind.Unknown
                && TryDetectRecursiveCompositeInner(underlying, conventionContext) is { } recursive)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DescriptorId: "ZAO052",
                    Location: location,
                    MessageArgs: new EquatableArray<string>(ImmutableArray.Create(
                        methodName,
                        recursive.OuterTypeDisplay,
                        recursive.InnerCtorArgName))));
                return null;
            }

            string? reader = resolution.Kind switch
            {
                ConventionKind.Primitive => PrimitiveCatalog.GetScalarReaderMethod(underlying),
                ConventionKind.ValueObject or ConventionKind.SingleArgCtor or ConventionKind.StaticFactory
                    => ResolveUnderlyingReaderForFactory(resolution),
                ConventionKind.Enum => ResolveUnderlyingReaderForEnum(underlying),
                ConventionKind.EnumAsString => "GetString",
                _ => null,
            };
            if (reader is null) return null;

            var isNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated
                || (p.Type is INamedTypeSymbol pn
                    && pn.IsGenericType
                    && pn.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T);

            var convention = BuildConventionInfo(underlying, resolution, reader);

            // Column name = PascalCased ctor-param name. C# convention has ctor params
            // in camelCase (`customerId`); SQL matches case-insensitively by default
            // but emitting the PascalCased form keeps generated source readable and
            // mirrors what a hand-written reader would do.
            var columnName = ToPascalCase(p.Name);

            // TypeName stores the UNWRAPPED type (see FlatRow for rationale).
            var unwrappedDisplay = underlying
                .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                .ToDisplayString(TypeDisplayFormat);

            columns.Add(new ColumnBinding(
                GetterMethod: reader,
                IsNullable: isNullable,
                TypeName: unwrappedDisplay,
                Convention: convention,
                ColumnName: columnName));
        }

        return new MaterializationModel(
            Kind: MaterializationKind.DomainEntity,
            TargetTypeFullName: named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Columns: new EquatableArray<ColumnBinding>(columns.MoveToImmutable()));
    }

    // v0.3 Phase B — classify a tuple return type's elements for multi-result-set
    // emit. Each tuple element drives one reader result set and resolves to a
    // MultiResultElement (Scalar / Row / List). Returns null if any element fails
    // to classify so the surrounding ClassifyEmitShape can fall through to Unknown.
    //
    // List<T>/IReadOnlyList<T>/IEnumerable<T> are accepted as List-kind elements; the
    // inner T must be a record (FlatRow) or a class with a single public ctor
    // (DomainEntity). Single record/class elements are Row-kind; primitive / enum /
    // value-object elements are Scalar-kind.
    private static MultiResultMaterializationModel? TryBuildMultiResultMaterialization(
        INamedTypeSymbol tupleType,
        bool returnsNullable,
        ConventionContext conventionContext)
    {
        var elementsBuilder = ImmutableArray.CreateBuilder<MultiResultElement>(tupleType.TupleElements.Length);
        var typeDisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        foreach (var field in tupleType.TupleElements)
        {
            var element = TryClassifyTupleElement(field.Name, field.Type, conventionContext, typeDisplayFormat);
            if (element is null) return null;
            elementsBuilder.Add(element);
        }

        // Tuple type display carries the outer nullable annotation when applicable; the
        // partial method signature needs the `?` to match the user declaration verbatim.
        var tupleDisplay = tupleType.ToDisplayString(typeDisplayFormat);
        if (returnsNullable && !tupleDisplay.EndsWith("?", System.StringComparison.Ordinal))
            tupleDisplay += "?";

        return new MultiResultMaterializationModel(
            TupleTypeDisplay: tupleDisplay,
            ReturnsNullable: returnsNullable,
            Elements: new EquatableArray<MultiResultElement>(elementsBuilder.MoveToImmutable()));
    }

    // v0.4 Phase E.1 — classify a tuple return type as an output-parameter
    // shape. Returns null when ZERO tuple fields match a C# parameter name
    // (caller falls through to the regular MultiResultSet path) or when one
    // of the result-position elements (tuple fields with no matching parameter)
    // fails to classify (mirrors TryBuildMultiResultMaterialization's bail
    // semantics — the caller treats this as "shape not yet emittable" and the
    // existing ZAO022 surface fires).
    //
    // Detection ladder per tuple element:
    //   * Element name matches (case-insensitive) a C# parameter name
    //     -> Output position. Build SprocOutputParam from the element's
    //        type via ConventionDiscovery (primitive / VO / enum funnel).
    //   * No matching C# parameter
    //     -> Result position. Reuse TryClassifyTupleElement so single-row /
    //        list / scalar shapes flow through unchanged.
    //
    // The output-only sub-case (every tuple field matches a parameter) is
    // valid — ResultElements is empty in that case and the Phase E.3 emit
    // routes through ExecuteNonQueryAsync instead of ExecuteReaderAsync.
    //
    // Cancellation-token parameters are NOT eligible matches — they're a
    // control signal, never bound as DbParameters. The match check filters
    // them out via the same IsCancellationToken comparison the parameter
    // binding emit uses.
    // Lightweight pre-check used by the classifier to detect "this tuple WOULD
    // have classified as SprocWithOutputParams if not for the outer nullable
    // annotation". Mirrors TryBuildSprocOutputParamsMaterialization's parameter
    // lookup (case-insensitive, CT excluded) but only reports a boolean so the
    // rejection branch can decide whether to surface ZAO022 vs fall through to
    // MultiResultSet. Cheaper than building the full model since we already
    // know we're about to reject.
    private static bool SprocHasAnyOutputParamMatch(
        INamedTypeSymbol tupleType,
        ImmutableArray<IParameterSymbol> methodParameters)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in methodParameters)
        {
            if (string.Equals(p.Type.ToDisplayString(),
                "System.Threading.CancellationToken", StringComparison.Ordinal))
            {
                continue;
            }
            names.Add(p.Name);
        }
        foreach (var field in tupleType.TupleElements)
        {
            if (names.Contains(field.Name)) return true;
        }
        return false;
    }

    private static SprocOutputParamsMaterializationModel? TryBuildSprocOutputParamsMaterialization(
        INamedTypeSymbol tupleType,
        bool returnsNullable,
        ImmutableArray<IParameterSymbol> methodParameters,
        ConventionContext conventionContext)
    {
        // Build a quick lookup of bindable parameter names. CancellationToken
        // parameters are excluded — they're a control signal, never bound as
        // DbParameters and never an output-position target. Case-insensitive
        // matching follows the design doc convention (parameter `newOrderId`
        // matches tuple field `NewOrderId`).
        var paramLookup = new Dictionary<string, IParameterSymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in methodParameters)
        {
            if (string.Equals(p.Type.ToDisplayString(),
                "System.Threading.CancellationToken", StringComparison.Ordinal))
            {
                continue;
            }
            // First-write wins on duplicate names (C# itself forbids duplicate
            // parameter names so this loop sees at most one entry per name).
            paramLookup[p.Name] = p;
        }

        var typeDisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        var outputBuilder = ImmutableArray.CreateBuilder<SprocOutputParam>();
        var resultBuilder = ImmutableArray.CreateBuilder<MultiResultElement>();
        var orderBuilder = ImmutableArray.CreateBuilder<SprocTupleSlot>(tupleType.TupleElements.Length);
        var matchedAny = false;

        foreach (var field in tupleType.TupleElements)
        {
            if (paramLookup.TryGetValue(field.Name, out var matchingParam))
            {
                // Output position. Resolve the element type's convention so the
                // emit can wrap the boxed `.Value` in a Convert.ToXxx + optional
                // VO factory call — same convention plumbing as scalar materialization.
                var elementType = field.Type;
                var isNullable = elementType.NullableAnnotation == NullableAnnotation.Annotated
                    || (elementType is INamedTypeSymbol pn
                        && pn.IsGenericType
                        && pn.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T);
                var unwrapped = UnwrapNullableValueType(elementType)
                    .WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                var resolution = ConventionDiscovery.Resolve(unwrapped, conventionContext);
                string? reader = resolution.Kind switch
                {
                    ConventionKind.Primitive => PrimitiveCatalog.GetScalarReaderMethod(unwrapped),
                    ConventionKind.ValueObject or ConventionKind.SingleArgCtor or ConventionKind.StaticFactory
                        => ResolveUnderlyingReaderForFactory(resolution),
                    ConventionKind.Enum => ResolveUnderlyingReaderForEnum(unwrapped),
                    ConventionKind.EnumAsString => "GetString",
                    _ => null,
                };
                if (reader is null) return null;
                var convention = BuildConventionInfo(unwrapped, resolution, reader);

                outputBuilder.Add(new SprocOutputParam(
                    TupleFieldName: field.Name,
                    MatchingParameterName: matchingParam.Name,
                    TypeName: unwrapped.ToDisplayString(typeDisplayFormat),
                    IsNullable: isNullable,
                    Convention: convention));
                orderBuilder.Add(new SprocTupleSlot(
                    SprocTupleSlotKind.Output, outputBuilder.Count - 1));
                matchedAny = true;
            }
            else
            {
                // Result position. Route through TryClassifyTupleElement so
                // single-row / list / scalar shapes flow through unchanged.
                var resultElement = TryClassifyTupleElement(
                    field.Name, field.Type, conventionContext, typeDisplayFormat);
                if (resultElement is null) return null;
                resultBuilder.Add(resultElement);
                orderBuilder.Add(new SprocTupleSlot(
                    SprocTupleSlotKind.Result, resultBuilder.Count - 1));
            }
        }

        if (!matchedAny)
        {
            // Zero matches — caller falls back to TryBuildMultiResultMaterialization
            // (the existing Phase D multi-result-set path).
            return null;
        }

        // Tuple type display carries the outer nullable annotation when applicable.
        var tupleDisplay = tupleType.ToDisplayString(typeDisplayFormat);
        if (returnsNullable && !tupleDisplay.EndsWith("?", System.StringComparison.Ordinal))
            tupleDisplay += "?";

        return new SprocOutputParamsMaterializationModel(
            TupleTypeDisplay: tupleDisplay,
            OutputElements: new EquatableArray<SprocOutputParam>(outputBuilder.ToImmutable()),
            ResultElements: new EquatableArray<MultiResultElement>(resultBuilder.ToImmutable()),
            TupleElementOrder: new EquatableArray<SprocTupleSlot>(orderBuilder.ToImmutable()));
    }

    // Classify ONE tuple element. Order of attempts mirrors the single-shape path:
    //   1. List<T> / IReadOnlyList<T> / IEnumerable<T> -> List element (record/class T).
    //   2. Record T -> Row element (FlatRow positional).
    //   3. Class T with single public ctor -> Row element (DomainEntity column-name).
    //   4. Primitive / Enum / Value-object -> Scalar element.
    // Returns null when no rule matches so the caller can bail the whole tuple.
    private static MultiResultElement? TryClassifyTupleElement(
        string tupleFieldName,
        ITypeSymbol elementType,
        ConventionContext conventionContext,
        SymbolDisplayFormat typeDisplayFormat)
    {
        // Strip the outer nullable annotation; per-element nullability flows via
        // IsNullable so the emit can wrap reads in IsDBNull guards.
        var isNullable = elementType.NullableAnnotation == NullableAnnotation.Annotated
            || (elementType is INamedTypeSymbol n
                && n.IsGenericType
                && n.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T);
        var unwrapped = UnwrapNullableValueType(elementType)
            .WithNullableAnnotation(NullableAnnotation.NotAnnotated);

        // (1) List-kind: the element type is a recognized list-like generic of arity 1.
        if (unwrapped is INamedTypeSymbol listLike
            && listLike.IsGenericType
            && listLike.TypeArguments.Length == 1
            && IsListLikeName(listLike))
        {
            var inner = listLike.TypeArguments[0]
                .WithNullableAnnotation(NullableAnnotation.NotAnnotated);

            // Inner element must materialize as a row — FlatRow or DomainEntity.
            var flat = TryBuildFlatRowMaterialization(inner, conventionContext);
            if (flat is not null)
            {
                return new MultiResultElement(
                    Kind: MultiResultElementKind.List,
                    TupleFieldName: tupleFieldName,
                    ElementTypeName: inner.ToDisplayString(typeDisplayFormat),
                    GetterMethod: null,
                    Convention: null,
                    IsNullable: false,
                    Columns: flat.Columns);
            }
            var domain = TryBuildDomainEntityMaterialization(inner, conventionContext);
            if (domain is not null)
            {
                return new MultiResultElement(
                    Kind: MultiResultElementKind.List,
                    TupleFieldName: tupleFieldName,
                    ElementTypeName: inner.ToDisplayString(typeDisplayFormat),
                    GetterMethod: null,
                    Convention: null,
                    IsNullable: false,
                    Columns: domain.Columns);
            }
            return null;
        }

        // (2/3) Row-kind: a single record or class instance per result set. Records take
        // the FlatRow positional path; classes with a single ctor take the DomainEntity
        // column-name path.
        var rowFlat = TryBuildFlatRowMaterialization(unwrapped, conventionContext);
        if (rowFlat is not null)
        {
            return new MultiResultElement(
                Kind: MultiResultElementKind.Row,
                TupleFieldName: tupleFieldName,
                ElementTypeName: unwrapped.ToDisplayString(typeDisplayFormat),
                GetterMethod: null,
                Convention: null,
                IsNullable: isNullable,
                Columns: rowFlat.Columns);
        }
        var rowDomain = TryBuildDomainEntityMaterialization(unwrapped, conventionContext);
        if (rowDomain is not null)
        {
            return new MultiResultElement(
                Kind: MultiResultElementKind.Row,
                TupleFieldName: tupleFieldName,
                ElementTypeName: unwrapped.ToDisplayString(typeDisplayFormat),
                GetterMethod: null,
                Convention: null,
                IsNullable: isNullable,
                Columns: rowDomain.Columns);
        }

        // (4) Scalar-kind: primitive / enum / value-object. Reuses the same
        // reader-method derivation that single-row shapes use so the emit stays
        // consistent across shape boundaries.
        var resolution = ConventionDiscovery.Resolve(unwrapped, conventionContext);
        string? reader = resolution.Kind switch
        {
            ConventionKind.Primitive => PrimitiveCatalog.GetScalarReaderMethod(unwrapped),
            ConventionKind.ValueObject or ConventionKind.SingleArgCtor or ConventionKind.StaticFactory
                => ResolveUnderlyingReaderForFactory(resolution),
            ConventionKind.Enum => ResolveUnderlyingReaderForEnum(unwrapped),
            ConventionKind.EnumAsString => "GetString",
            _ => null,
        };
        if (reader is null) return null;

        var convention = BuildConventionInfo(unwrapped, resolution, reader);
        return new MultiResultElement(
            Kind: MultiResultElementKind.Scalar,
            TupleFieldName: tupleFieldName,
            ElementTypeName: unwrapped.ToDisplayString(typeDisplayFormat),
            GetterMethod: reader,
            Convention: convention,
            IsNullable: isNullable,
            Columns: EquatableArray<ColumnBinding>.Empty);
    }

    // Match List<T> / IReadOnlyList<T> / IList<T> / IEnumerable<T> / ICollection<T> /
    // IReadOnlyCollection<T> by metadata name. Tuple elements typed as one of these
    // generic collections route to the List-kind reader loop. Anything else (array,
    // HashSet, custom collection) is intentionally out of scope for v0.3 Phase B —
    // List<T> is the canonical accumulator and the others are recognized so the
    // common adopter shapes (e.g. IReadOnlyList<T>) flow through without forcing a
    // List<T> rewrite.
    private static bool IsListLikeName(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString();
        if (!string.Equals(ns, "System.Collections.Generic", System.StringComparison.Ordinal))
            return false;
        return type.Name is "List"
            or "IList"
            or "IReadOnlyList"
            or "IEnumerable"
            or "ICollection"
            or "IReadOnlyCollection";
    }

    // PascalCase a ctor parameter name for the SQL column lookup. C# parameter names
    // are camelCase by convention (`customerId`); the matching SQL column is typically
    // `CustomerId`. ASCII-only uppercase of the first letter — anything more elaborate
    // is over-engineered for v0.2. Names that are already capitalized pass through.
    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (char.IsUpper(name[0])) return name;
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    // For ValueObject / SingleArgCtor / StaticFactory the factory takes a single
    // primitive parameter. Map that primitive to its IDataReader.GetXxx method so the
    // emitter can do `Type.From(reader.GetInt32(N))`. Returns null if the factory's
    // parameter type isn't a known primitive.
    private static string? ResolveUnderlyingReaderForFactory(ConventionResult resolution)
    {
        ITypeSymbol? underlying = resolution.Factory switch
        {
            IMethodSymbol m when m.Parameters.Length == 1 => m.Parameters[0].Type,
            _ => null,
        };
        return underlying is null ? null : PrimitiveCatalog.GetScalarReaderMethod(underlying);
    }

    // Enum's underlying integral type drives both the column-read (GetInt32 by
    // default; GetByte / GetInt16 / GetInt64 for byte / short / long backed enums)
    // AND the parameter-bind cast (`(int)@status`). Centralized here so the column
    // emit and parameter emit pull from one place. Returns null only if the type
    // isn't actually an enum or has an unrecognized underlying primitive.
    private static string? ResolveUnderlyingReaderForEnum(ITypeSymbol enumType)
    {
        if (enumType is not INamedTypeSymbol named || named.TypeKind != TypeKind.Enum)
            return null;
        var underlying = named.EnumUnderlyingType;
        if (underlying is null) return null;
        return PrimitiveCatalog.GetScalarReaderMethod(underlying);
    }

    // Project a ConventionResult onto a cache-safe ConventionInfo. The string-only
    // shape lets QueryMethodModel/MaterializationModel stay equatable across
    // incremental-generator runs — the symbols on ConventionResult are NOT cache-safe.
    // Returns null when the resolution is Primitive (or anything else the v0.1 emit
    // path already handles without auxiliary discovery data).
    private static ConventionInfo? BuildConventionInfo(
        ITypeSymbol resolvedType,
        ConventionResult resolution,
        string? underlyingReader)
    {
        switch (resolution.Kind)
        {
            case ConventionKind.ValueObject:
            case ConventionKind.StaticFactory:
                {
                    if (resolution.Factory is not IMethodSymbol factory) return null;
                    var typeFqn = resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var factoryFqn = typeFqn + "." + factory.Name;
                    return new ConventionInfo(
                        Kind: (int)resolution.Kind,
                        FactoryFullName: factoryFqn,
                        FactoryIsCtor: false,
                        ValuePropertyName: resolution.Value?.Name,
                        UnderlyingReader: underlyingReader);
                }
            case ConventionKind.SingleArgCtor:
                {
                    var typeFqn = resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return new ConventionInfo(
                        Kind: (int)resolution.Kind,
                        FactoryFullName: typeFqn,
                        FactoryIsCtor: true,
                        ValuePropertyName: resolution.Value?.Name,
                        UnderlyingReader: underlyingReader);
                }
            case ConventionKind.Enum:
            case ConventionKind.EnumAsString:
                {
                    // Enums have no factory method or unwrap property — the emitter
                    // renders a cast (`Enum`) or `Enum.Parse<T>(...)` (`EnumAsString`)
                    // directly. FactoryFullName carries the enum's globally-qualified
                    // type name so the emitter doesn't have to re-format it; ValueProperty
                    // is intentionally null. Parameter binding inspects Kind: int-backed
                    // enums emit `(int)@x`, string-backed enums emit `@x.ToString()`.
                    var typeFqn = resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return new ConventionInfo(
                        Kind: (int)resolution.Kind,
                        FactoryFullName: typeFqn,
                        FactoryIsCtor: false,
                        ValuePropertyName: null,
                        UnderlyingReader: underlyingReader);
                }
            default:
                return null;
        }
    }

    // v0.5 Phase B — flatten a MultiArgCtor convention into the per-field model
    // the composite-binding emit consumes. Returns `default` (IsDefault, length 0)
    // when any inner ctor parameter fails to resolve to a known binding strategy —
    // the caller falls through to ZAO041 in that case so adopters see a real
    // diagnostic instead of a silently-misshaped emit.
    //
    // Recursive composites (an inner ctor arg whose type is itself MultiArgCtor)
    // are also rejected here. Phase B's contract is one-level positional unpack;
    // a nested composite would need a different SQL-side naming convention
    // (`@total_address_street`?) and is intentionally deferred.
    private static EquatableArray<CompositeBindingField> BuildCompositeFields(
        ConventionResult resolution,
        ConventionContext conventionContext)
    {
        var innerBuilder = ImmutableArray.CreateBuilder<CompositeBindingField>(resolution.ExpandedColumns.Length);
        foreach (var innerParam in resolution.ExpandedColumns)
        {
            var innerUnderlying = UnwrapNullableValueType(innerParam.Type);
            var innerResolution = ConventionDiscovery.Resolve(innerUnderlying, conventionContext);

            if (innerResolution.Kind == ConventionKind.Unknown
                || innerResolution.Kind == ConventionKind.MultiArgCtor)
            {
                return default;
            }

            var innerReader = innerResolution.Kind switch
            {
                ConventionKind.ValueObject or ConventionKind.SingleArgCtor or ConventionKind.StaticFactory
                    => ResolveUnderlyingReaderForFactory(innerResolution),
                ConventionKind.Enum => ResolveUnderlyingReaderForEnum(innerUnderlying),
                ConventionKind.EnumAsString => "GetString",
                _ => null,
            };
            var innerConvention = BuildConventionInfo(innerUnderlying, innerResolution, innerReader);

            var innerIsNullable = innerParam.Type.NullableAnnotation == NullableAnnotation.Annotated
                || (innerParam.Type is INamedTypeSymbol ipn
                    && ipn.IsGenericType
                    && ipn.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T);

            innerBuilder.Add(new CompositeBindingField(
                CtorArgName: innerParam.Name,
                IsNullable: innerIsNullable,
                Convention: innerConvention));
        }
        return new EquatableArray<CompositeBindingField>(innerBuilder.MoveToImmutable());
    }

    // Read the optional `Name` argument from `[ZeroAlloc.ORM.ParamAttribute]` on a
    // method parameter. Returns null when the attribute is absent or doesn't set Name.
    // The named argument is a string literal; null/empty values fall back to the C# name.
    private static string? ReadParamNameOverride(IParameterSymbol p)
    {
        foreach (var attr in p.GetAttributes())
        {
            if (!string.Equals(
                attr.AttributeClass?.ToDisplayString(),
                "ZeroAlloc.ORM.ParamAttribute",
                StringComparison.Ordinal))
            {
                continue;
            }
            foreach (var kvp in attr.NamedArguments)
            {
                if (!string.Equals(kvp.Key, "Name", StringComparison.Ordinal)) continue;
                if (kvp.Value.Value is string s && !string.IsNullOrEmpty(s))
                    return s;
            }
        }
        return null;
    }

    // v0.5 Phase D — read the `Factory` named arg from a `[Materialize(...)]`
    // attribute on the given symbol. Returns null when the attribute is absent
    // or when Factory is unset / null / empty. The attribute itself is found
    // by fully-qualified display name so the lookup is robust against alias
    // / partial-namespace usage at the call site.
    private static string? ReadMaterializeFactoryName(ImmutableArray<AttributeData> attrs)
    {
        foreach (var attr in attrs)
        {
            if (!string.Equals(
                attr.AttributeClass?.ToDisplayString(),
                "ZeroAlloc.ORM.MaterializeAttribute",
                StringComparison.Ordinal))
            {
                continue;
            }
            foreach (var kvp in attr.NamedArguments)
            {
                if (!string.Equals(kvp.Key, "Factory", StringComparison.Ordinal)) continue;
                if (kvp.Value.Value is string s && !string.IsNullOrEmpty(s))
                    return s;
            }
        }
        return null;
    }

    // Post-review Fix 8 — detect a `[Materialize(...)]` attribute that sets
    // `Strategy = Custom` without a `Factory` argument. Returns true when the
    // adopter has explicitly requested Custom but provided no factory name —
    // a silent no-op today, which ZAO043 surfaces with a tailored reason.
    private static bool HasCustomStrategyWithoutFactory(ImmutableArray<AttributeData> attrs)
    {
        foreach (var attr in attrs)
        {
            if (!string.Equals(
                attr.AttributeClass?.ToDisplayString(),
                "ZeroAlloc.ORM.MaterializeAttribute",
                StringComparison.Ordinal))
            {
                continue;
            }
            string? factory = null;
            // MaterializeStrategy.Custom == 3 (Auto=0, FlatRow=1, DomainEntity=2, Custom=3).
            int? strategy = null;
            foreach (var kvp in attr.NamedArguments)
            {
                if (string.Equals(kvp.Key, "Factory", StringComparison.Ordinal))
                {
                    if (kvp.Value.Value is string s && !string.IsNullOrEmpty(s))
                        factory = s;
                }
                else if (string.Equals(kvp.Key, "Strategy", StringComparison.Ordinal))
                {
                    if (kvp.Value.Value is int i) strategy = i;
                }
            }
            if (strategy == 3 && factory is null) return true;
        }
        return false;
    }

    // Lookup the static factory method named `factoryName` on `targetType`. Returns
    // the matched IMethodSymbol when found, or null when missing / non-static. Picks
    // the first parameterized public/static match — multi-arg-overload selection by
    // signature is deferred to v0.6+ (today's contract: a single static factory of
    // the given name with > 0 parameters wins).
    private static IMethodSymbol? LookupStaticFactory(ITypeSymbol targetType, string factoryName)
        => LookupStaticFactory(targetType, factoryName, out _, out _);

    // Post-review Fix 3 — out-param overload reporting. The classifier needs to
    // know when MULTIPLE static methods of the same name match so it can fire
    // ZAO044 (overload-selection-by-signature is non-deterministic and we will
    // not silently pick the first one). Returns the first match for backward
    // compatibility; `matchCount` reports how many total static matches existed.
    // `failureReason` is non-null when no callable static factory was found,
    // and names the closest miss ("method not found", "method is not static",
    // "method is not public") so ZAO043 can surface an actionable message.
    private static IMethodSymbol? LookupStaticFactory(
        ITypeSymbol targetType,
        string factoryName,
        out int matchCount,
        out string? failureReason)
    {
        matchCount = 0;
        IMethodSymbol? first = null;
        // Track the closest miss reason so the caller can surface it via ZAO043.
        bool sawAnyMethod = false;
        bool sawNonStatic = false;
        bool sawWrongAccessibility = false;
        foreach (var member in targetType.GetMembers(factoryName))
        {
            if (member is not IMethodSymbol method) continue;
            sawAnyMethod = true;
            if (!method.IsStatic) { sawNonStatic = true; continue; }
            if (method.DeclaredAccessibility != Accessibility.Public
                && method.DeclaredAccessibility != Accessibility.Internal)
            {
                sawWrongAccessibility = true;
                continue;
            }
            if (method.Parameters.Length == 0) continue;
            matchCount++;
            first ??= method;
        }
        if (first is not null)
        {
            failureReason = null;
            return first;
        }
        failureReason = !sawAnyMethod ? "method not found"
            : sawNonStatic ? "method is not static"
            : sawWrongAccessibility ? "method is not public"
            : "method not found";
        return null;
    }

    // Resolve the `[Materialize(Factory = "X")]` annotation for a return-position
    // type. Checks the method's return-value attributes first (highest priority —
    // method-level overrides type-level so adopters can swap factories per method),
    // falling back to the target-type's own attributes. Returns the factory name
    // when present (regardless of whether the static method exists — that check
    // happens at LookupStaticFactory).
    private static string? ResolveReturnTypeFactoryName(IMethodSymbol method, ITypeSymbol returnInnerType)
    {
        // `[return: Materialize(Factory = "X")]` lives on the method's return value.
        var methodLevel = ReadMaterializeFactoryName(method.GetReturnTypeAttributes());
        if (methodLevel is not null) return methodLevel;
        return ReadMaterializeFactoryName(returnInnerType.GetAttributes());
    }

    // Peel `Nullable<T>` to `T`; otherwise return the input unchanged.
    private static ITypeSymbol UnwrapNullableValueType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named
            && named.IsGenericType
            && named.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T)
        {
            return named.TypeArguments[0];
        }
        return type;
    }

    // Map a supported primitive scalar type to the IDataReader.GetXxx method that
    // strongly-typed-reads it. Thin pass-through to PrimitiveCatalog so the rest of
    // the generator keeps its existing call shape; the canonical table lives in
    // ZeroAlloc.TypeConversions.PrimitiveCatalog.
    private static string? GetScalarPrimitiveReaderInfo(ITypeSymbol type)
        => PrimitiveCatalog.GetScalarReaderMethod(type);

    private static (string Access, bool Resolved) ResolveConnectionAccess(INamedTypeSymbol containing)
    {
        // (a) Primary ctor param. Only primary-ctor parameters are captured as state
        // accessible from methods; ordinary ctor parameters go out of scope. Detect
        // primary-ctor params by inspecting the declaring syntax: a primary ctor's
        // parameter list is attached directly to a TypeDeclarationSyntax (class/struct/record),
        // not to a ConstructorDeclarationSyntax.
        foreach (var ctor in containing.InstanceConstructors)
        {
            foreach (var p in ctor.Parameters)
            {
                if (!IsIAsyncDbConnection(p.Type))
                    continue;
                if (IsPrimaryConstructorParameter(p))
                    return (p.Name, true);
            }
        }

        // (b) Field or (c) get-only property of the right type. Skip compiler-synthesized
        // members (e.g. auto-property backing fields, primary-ctor capture fields) so we
        // return the user-facing identifier instead of `<Foo>k__BackingField`.
        // First match in source-declaration order wins; ambiguity (multiple connection
        // sources) will be caught by a future ZAO00x diagnostic.
        foreach (var member in containing.GetMembers())
        {
            if (member.IsImplicitlyDeclared) continue;
            if (member is IFieldSymbol f && IsIAsyncDbConnection(f.Type))
                return (f.Name, true);
            if (member is IPropertySymbol pr && IsIAsyncDbConnection(pr.Type))
                return (pr.Name, true);
        }

        // Fallback; ZAO003 surfaces an error so the user sees the missing source.
        return ("connection", false);
    }

    private static bool IsSupportedReturnType(ITypeSymbol returnType)
    {
        var name = returnType.Name;
        var arity = (returnType as INamedTypeSymbol)?.Arity ?? 0;
        return (name, arity) is ("Task", 0) or ("Task", 1)
            or ("ValueTask", 0) or ("ValueTask", 1)
            or ("IAsyncEnumerable", 1);
    }

    // Resolve the emit-template selector for a [Query] method body. Single-
    // statement SQL is always SingleCommand; multi-statement SQL routes by
    // the BatchMode int value mirrored from ZeroAlloc.ORM.BatchMode
    // (0=Auto, 1=Always, 2=Never). Phase B will consume the resulting strategy
    // at emit time; Phase A only populates the field on the model.
    private static BatchEmitStrategy ResolveBatchStrategy(string sql, int batchMode)
    {
        var statementCount = SqlStatementSplitter.CountStatements(sql);
        if (statementCount <= 1) return BatchEmitStrategy.SingleCommand;

        return batchMode switch
        {
            0 => BatchEmitStrategy.BatchWithFallback,
            1 => BatchEmitStrategy.BatchAlways,
            2 => BatchEmitStrategy.JoinedStatementsOnly,
            _ => BatchEmitStrategy.BatchWithFallback,
        };
    }

    private static bool IsMultiResultReturnType(ITypeSymbol returnType)
    {
        // Peel Task<T> / ValueTask<T> wrappers to inspect the element type.
        var inner = UnwrapAsyncWrapper(returnType);
        if (inner is null) return false;
        // v0.3 Phase B — `Task<(...)?>` is the canonical multi-result return shape;
        // peel the Nullable<T> wrapper so the tuple check below sees the ValueTuple
        // directly rather than the surrounding `Nullable<...>` generic.
        inner = UnwrapNullableValueType(inner);
        if (inner is INamedTypeSymbol named)
        {
            // ValueTuple (sugar form `(T1, T2)`) — Roslyn surfaces this via IsTupleType.
            if (named.IsTupleType) return true;
            // System.Tuple<T1, T2, ...> — constructed-from check.
            var constructedFrom = named.ConstructedFrom?.ToDisplayString();
            if (constructedFrom is not null && constructedFrom.StartsWith("System.Tuple<", System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // Peel the async wrapper (Task<T> / ValueTask<T> / IAsyncEnumerable<T>) AND the
    // nullable-reference annotation to surface the element type that the materializer
    // would actually construct. Returns null for non-generic Task / ValueTask (no
    // element type to discover) and for return types that don't match the surface
    // shape — caller decides whether the diagnostic still applies.
    private static ITypeSymbol? TryGetReturnElementType(ITypeSymbol returnType)
    {
        if (returnType is not INamedTypeSymbol named) return null;
        if (!named.IsGenericType) return null;
        if (named.Name is not ("Task" or "ValueTask" or "IAsyncEnumerable")) return null;
        if (named.TypeArguments.Length != 1) return null;
        var element = named.TypeArguments[0];
        // Peel `T?` (nullable reference) and `Nullable<T>` (value-type wrapper) to
        // get the bare element type. ConventionDiscovery doesn't care about
        // nullability; it only classifies the underlying construction shape.
        element = element.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        element = UnwrapNullableValueType(element);
        return element;
    }

    private static ITypeSymbol? UnwrapAsyncWrapper(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named) return type;
        if (!named.IsGenericType) return type;
        return (named.Name, named.Arity) switch
        {
            ("Task", 1) or ("ValueTask", 1) => named.TypeArguments[0],
            _ => type,
        };
    }

    private static bool IsIAsyncEnumerable(ITypeSymbol returnType)
    {
        var arity = (returnType as INamedTypeSymbol)?.Arity ?? 0;
        return string.Equals(returnType.Name, "IAsyncEnumerable", StringComparison.Ordinal) && arity == 1;
    }

    private static bool IsIAsyncDbConnection(ITypeSymbol type)
    {
        // Two-tier match:
        //   1. Fully-qualified `System.Data.Async.IAsyncDbConnection` — production path
        //      once the abstraction is resolvable in the consumer's compilation.
        //   2. Simple-name fallback for error-types — until Phase 3's ZAO003 lands a
        //      proper symbol-based check, test compilations see the type as unresolved
        //      and its display string collapses to the simple name. We only accept
        //      this when the namespace is empty (genuine error type) OR matches the
        //      expected `System.Data.Async`, never on arbitrary user-defined types.
        var display = type.ToDisplayString();
        if (string.Equals(display, IAsyncDbConnectionFullName, StringComparison.Ordinal))
            return true;
        if (string.Equals(display, IAsyncDbConnectionSimpleName, StringComparison.Ordinal))
        {
            var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            return ns is "" or "<global namespace>" or "System.Data.Async";
        }
        return false;
    }

    private static bool IsPrimaryConstructorParameter(IParameterSymbol p)
    {
        foreach (var syntaxRef in p.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            // ParameterSyntax -> ParameterListSyntax -> TypeDeclarationSyntax means primary ctor.
            // For a regular ctor, the grandparent is ConstructorDeclarationSyntax.
            if (syntax.Parent?.Parent is TypeDeclarationSyntax)
                return true;
        }
        return false;
    }

    private static DiagnosticDescriptor? LookupDescriptor(string id) => id switch
    {
        "ZAO001" => DiagnosticDescriptors.ZAO001_NotPartial,
        "ZAO002" => DiagnosticDescriptors.ZAO002_BadReturnType,
        "ZAO003" => DiagnosticDescriptors.ZAO003_NoConnection,
        "ZAO004" => DiagnosticDescriptors.ZAO004_TypeNotPartial,
        "ZAO005" => DiagnosticDescriptors.ZAO005_MultipleAttributes,
        "ZAO006" => DiagnosticDescriptors.ZAO006_MultipleCancellationTokens,
        "ZAO007" => DiagnosticDescriptors.ZAO007_MissingEnumeratorCancellation,
        "ZAO008" => DiagnosticDescriptors.ZAO008_SingleResultWithSemicolons,
        "ZAO009" => DiagnosticDescriptors.ZAO009_RedundantAsync,
        "ZAO020" => DiagnosticDescriptors.ZAO020_FromResourceNotImplemented,
        "ZAO022" => DiagnosticDescriptors.ZAO022_UnknownReturnShape,
        "ZAO032" => DiagnosticDescriptors.ZAO032_TupleArityExceedsStatements,
        "ZAO033" => DiagnosticDescriptors.ZAO033_StatementsExceedTupleArity,
        "ZAO040" => DiagnosticDescriptors.ZAO040_NoConstructionStrategy,
        "ZAO041" => DiagnosticDescriptors.ZAO041_NoUnwrapStrategy,
        "ZAO042" => DiagnosticDescriptors.ZAO042_StoreAsStringNonEnum,
        "ZAO043" => DiagnosticDescriptors.ZAO043_MaterializeFactoryMissing,
        "ZAO044" => DiagnosticDescriptors.ZAO044_AmbiguousDiscovery,
        "ZAO051" => DiagnosticDescriptors.ZAO051_FactoryParameterColumnMismatch,
        "ZAO052" => DiagnosticDescriptors.ZAO052_RecursiveCompositeDeferred,
        "ZAO050" => DiagnosticDescriptors.ZAO050_NullableCompositeRuntimeCheck,
        "ZAO060" => DiagnosticDescriptors.ZAO060_OutOrRefOnAsync,
        "ZAO061" => DiagnosticDescriptors.ZAO061_EmptyProcedureName,
        "ZAO062" => DiagnosticDescriptors.ZAO062_TupleFieldNotMatchingParameter,
        "ZAO063" => DiagnosticDescriptors.ZAO063_ParamNameOnCompositeUnsupported,
        "ZAO064" => DiagnosticDescriptors.ZAO064_BatchOnStoredProcedureIgnored,
        _ => null,
    };

    private static bool ReportDiagnostics(SourceProductionContext context, QueryRepositoryModel repo)
    {
        var hadError = false;
        foreach (var method in repo.Methods)
        {
            foreach (var diag in method.Diagnostics)
            {
                var descriptor = LookupDescriptor(diag.DescriptorId);
                if (descriptor is null) continue;
                if (descriptor.DefaultSeverity == DiagnosticSeverity.Error) hadError = true;
                object[] args = diag.MessageArgs.Values.IsDefault
                    ? Array.Empty<object>()
                    : diag.MessageArgs.Values.ToArray();
                context.ReportDiagnostic(Diagnostic.Create(
                    descriptor,
                    diag.Location?.ToLocation(),
                    args));
            }
        }

        // Type-scoped diagnostics — emit once per repository, keyed at the containing
        // type. ZAO003 + ZAO004 read directly off QueryRepositoryModel (R8 hoist
        // removed the per-method redundancy and the "first-method-as-representative"
        // fallback).
        //
        // ZAO003 fires when no IAsyncDbConnection source is found on the containing
        // type. ZAO004 fires when the containing type itself isn't `partial`. When both
        // apply, ZAO004 is the dominant problem: a non-partial type cannot host generated
        // code at all, so the missing-connection guidance is premature noise. Surface
        // only ZAO004 in that combined case so the adopter sees one actionable error,
        // fixes it, and re-evaluates the rest (including a possibly still-missing
        // connection) on the next compile.
        if (!repo.ConnectionResolved && repo.ContainingTypePartial)
        {
            hadError = true;
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ZAO003_NoConnection,
                repo.ContainingTypeLocation?.ToLocation(),
                repo.ContainingTypeFullName));
        }
        if (!repo.ContainingTypePartial)
        {
            hadError = true;
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ZAO004_TypeNotPartial,
                repo.ContainingTypeLocation?.ToLocation(),
                repo.ContainingTypeFullName));
        }
        return hadError;
    }

    private static void EmitRepository(SourceProductionContext context, QueryRepositoryModel repo)
    {
        // Invariant: GroupBy never yields empty groups; repo.Methods is always non-empty.

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (repo.Namespace is { } ns)
        {
            sb.AppendLine($"namespace {ns};");
            sb.AppendLine();
        }
        sb.AppendLine($"partial class {repo.ContainingTypeName}");
        sb.AppendLine("{");
        var first = true;
        foreach (var m in repo.Methods)
        {
            if (!first) sb.AppendLine();
            first = false;
            switch (m.Shape)
            {
                case EmitShape.ScalarInt:
                    EmitScalarInt(sb, m, repo.ConnectionAccess);
                    break;
                case EmitShape.NullableScalar:
                    EmitNullableScalar(sb, m, repo.ConnectionAccess);
                    break;
                case EmitShape.FlatRow:
                    EmitFlatRow(sb, m, repo.ConnectionAccess);
                    break;
                case EmitShape.DomainEntity:
                    EmitDomainEntity(sb, m, repo.ConnectionAccess);
                    break;
                case EmitShape.Streaming:
                    // v0.3 Phase C.2 — yield-based async iterator. Element materialization
                    // reuses the FlatRow / DomainEntity model carried by m.Materialization;
                    // the surrounding emit owns the open/while/yield/close shape.
                    EmitStreaming(sb, m, repo.ConnectionAccess);
                    break;
                case EmitShape.ListResultSet:
                    // v1.2 — Task<IReadOnlyList<TRow>> buffered list return. Same
                    // FlatRow / DomainEntity element materialization as Streaming;
                    // body buffers into a List<TRow> instead of yielding.
                    EmitListResultSet(sb, m, repo.ConnectionAccess);
                    break;
                case EmitShape.CommandNonQuery:
                    // v0.4 Phase A.2 — [Command(Kind = NonQuery)] emit. Open/execute/
                    // close lifecycle around ExecuteNonQueryAsync. Task<int> shape
                    // returns the rows-affected count; Task / ValueTask shape awaits
                    // without a return.
                    EmitCommandNonQuery(sb, m, repo.ConnectionAccess);
                    break;
                case EmitShape.CommandScalar:
                    // v0.4 Phase B.1 — [Command(Kind = Scalar)] emit. Open/execute/
                    // close lifecycle around ExecuteScalarAsync. Result materialization
                    // is uniform across primitives, value-objects, single-arg-ctor
                    // records, and enums via the ConventionDiscovery integration
                    // captured on m.Materialization.
                    EmitCommandScalar(sb, m, repo.ConnectionAccess);
                    break;
                case EmitShape.CommandIdentity:
                    // v0.4 Phase C.1 — [Command(Kind = Identity)] emit. Same
                    // open/execute/close lifecycle around ExecuteScalarAsync as
                    // CommandScalar; the materialization helper is shared
                    // (EmitScalarMaterialization). Identity rejects nullable
                    // returns at classification time, and the null-guard message
                    // here references the RETURNING / SCOPE_IDENTITY() contract
                    // rather than offering a Task<T?> escape hatch.
                    EmitCommandIdentity(sb, m, repo.ConnectionAccess);
                    break;
                case EmitShape.Composite:
                    // v0.5 Phase A — composite at scalar position. Sentinel
                    // comment marks the classifier branch; the real emit body
                    // lands in EmitComposite. Wording (`flattened columns: N`)
                    // matches the nested FlatRow / DomainEntity sentinels (Fix 7).
                    // The `!.` (Fix 8) reflects the EmitComposite invariant that
                    // m.Materialization is non-null on this branch — the bail-on-null
                    // guard inside EmitComposite asserts the same.
                    {
                        var compCount = m.Materialization!.Columns.Length;
                        // v0.5 Phase D — factory dispatch shapes wear a distinct
                        // sentinel so the classifier branch is unambiguous in
                        // snapshots: `composite-factory T.Method (factory args: N)`
                        // vs the existing `composite T (flattened columns: N)`.
                        if (m.Materialization!.FactoryMethodName is { } factoryName)
                        {
                            sb.AppendLine($"    // EmitShape: composite-factory {m.Materialization!.TargetTypeFullName}.{factoryName} (factory args: {compCount.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
                        }
                        else
                        {
                            sb.AppendLine($"    // EmitShape: composite {m.Materialization!.TargetTypeFullName} (flattened columns: {compCount.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
                        }
                    }
                    EmitComposite(sb, m, repo.ConnectionAccess);
                    break;
                case EmitShape.SprocWithOutputParams:
                    // v0.4 Phase E — [StoredProcedure] returning a named tuple
                    // with at least one tuple field matching a C# parameter.
                    // The matching tuple positions emit Direction = Output on
                    // the bound parameter and read the value back from the
                    // parameter after the command finishes. The detection
                    // sentinel below lets StoredProcedureOutputParamsDetectionTests
                    // prove the classifier reached this branch without depending
                    // on the full emit body landing.
                    sb.AppendLine($"    // EmitShape.SprocWithOutputParams — {m.MethodName}");
                    EmitSprocWithOutputParams(sb, m, repo.ConnectionAccess);
                    break;
                case EmitShape.MultiResultSet:
                    // v0.3 Phase B — per-strategy dispatch:
                    //   BatchAlways           -> IAsyncDbBatch path (B.2)
                    //   JoinedStatementsOnly  -> ;-joined single-command fallback (B.3)
                    //   BatchWithFallback     -> runtime branch on CanCreateBatch (B.4)
                    //   anything else         -> stub (shouldn't reach here, defensive)
                    //
                    // v0.4 Phase D.3 — stored procedures carry empty SQL so
                    // ResolveBatchStrategy returns SingleCommand; route those to the
                    // joined-single-command path (EmitMultiResultSetJoined). That path
                    // already opens one DbCommand, executes ExecuteReaderAsync, and
                    // walks NextResultAsync per element — exactly what a multi-result
                    // sproc needs. BuildCommandTextAssignment (Phase D.2) flips the
                    // CommandText / CommandType lines for the sproc inside that emit
                    // path; no further sproc-specific code needed here.
                    if (m.IsStoredProcedure)
                    {
                        EmitMultiResultSetJoined(sb, m, repo.ConnectionAccess);
                        break;
                    }
                    switch (m.Strategy)
                    {
                        case BatchEmitStrategy.BatchAlways:
                            EmitMultiResultSetBatch(sb, m, repo.ConnectionAccess);
                            break;
                        case BatchEmitStrategy.JoinedStatementsOnly:
                            EmitMultiResultSetJoined(sb, m, repo.ConnectionAccess);
                            break;
                        case BatchEmitStrategy.BatchWithFallback:
                            EmitMultiResultSetBatchWithFallback(sb, m, repo.ConnectionAccess);
                            break;
                        default:
                            EmitMultiResultSetStub(sb, m);
                            break;
                    }
                    break;
                case EmitShape.BulkInsertCommand:
                    // v1.3 Task 5 — classifier landed; real chunked-VALUES emit
                    // arrives in Task 6. Emit a NotImplementedException-throwing
                    // partial method body so the adopter's build stays green (a
                    // partial method without a matching body would CS8795) while
                    // the runtime call surfaces the unimplemented shape loudly.
                    // The TODO comment is intentional so Task 6 replaces this in
                    // one place. Task 5's value is the classifier + diagnostics;
                    // the materialization model is wired through QueryMethodModel
                    // and ready for Task 6 to consume.
                    sb.AppendLine($"    {GeneratedCodeAttribute}");
                    sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({BuildParameterList(m.MethodParameters)})");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        // TODO Task 6 — emit chunked VALUES INSERT for {m.MethodName}");
                    sb.AppendLine("        await global::System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);");
                    sb.AppendLine($"        throw new global::System.NotImplementedException(\"BulkInsert emit (Task 6) not yet implemented for {m.MethodName}.\");");
                    sb.AppendLine("    }");
                    break;
                default:
                    // v0.4 Phase C.1 — all three CommandKind values (NonQuery /
                    // Scalar / Identity) now have real emit shapes. Any [Command]
                    // method reaching the default branch implies ClassifyEmitShape
                    // returned EmitShape.Unknown because the return type wasn't
                    // classifiable; ZAO002 already fires upstream (the IsCommand-
                    // specific block below the shape table) and the hadError gate
                    // skips emit at the repo level — so this branch is reachable
                    // only for non-Command Unknown shapes, kept as a TODO marker
                    // for the v0.1 "shape not yet implemented" path.
                    sb.AppendLine($"    // TODO: emit body for {m.MethodName} (uses {repo.ConnectionAccess}) -- v0.1 Task 4.x");
                    break;
            }
        }
        sb.AppendLine("}");

        var hint = $"{repo.ContainingTypeName}.g.cs";
        context.AddSource(hint, SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    // Shared connection-lifecycle prologue/epilogue for all single-method emit shapes.
    // Eight emit sites used to hand-write the identical open/try and finally/close blocks
    // — the helpers below collapse the duplication while keeping the byte-identical
    // output the snapshot harness depends on. The `indent` argument is the leading
    // whitespace per line; `ctRef` is the formatted CT expression (e.g. `ct`, `@event`,
    // or the literal `default`). `connectionAccess` is the bare member identifier
    // (`connection`) — the helper prefixes the `@` itself.
    private static void BuildConnectionPrologue(StringBuilder sb, string connectionAccess, string ctRef, string indent)
    {
        sb.Append(indent).Append("var __conn = @").Append(connectionAccess).AppendLine(";");
        sb.Append(indent).AppendLine("var __openedHere = __conn.State != global::System.Data.ConnectionState.Open;");
        sb.Append(indent).Append("if (__openedHere) await __conn.OpenAsync(").Append(ctRef).AppendLine(").ConfigureAwait(false);");
        sb.Append(indent).AppendLine("try");
        sb.Append(indent).AppendLine("{");
    }

    private static void BuildConnectionEpilogue(StringBuilder sb, string indent)
    {
        sb.Append(indent).AppendLine("}");
        sb.Append(indent).AppendLine("finally");
        sb.Append(indent).AppendLine("{");
        sb.Append(indent).AppendLine("    if (__openedHere) await __conn.CloseAsync().ConfigureAwait(false);");
        sb.Append(indent).AppendLine("}");
    }

    // v0.4 Phase D — emit the `__cmd.CommandText = ...;` line plus, for stored
    // procedures, the immediately-following `__cmd.CommandType = StoredProcedure;`
    // line. Centralizes the sproc/query branch so every single-command emit shape
    // (ScalarInt / NullableScalar / FlatRow / DomainEntity / CommandNonQuery /
    // CommandScalar / CommandIdentity / Streaming / MultiResultSetJoined) picks up
    // the sproc flip without duplicating the conditional.
    //
    // For a [Query] / [Command] method (`m.IsStoredProcedure == false`):
    //   __cmd.CommandText = "<SQL literal>";
    //
    // For a [StoredProcedure] method (`m.IsStoredProcedure == true`):
    //   __cmd.CommandText = "<procedure name>";
    //   __cmd.CommandType = global::System.Data.CommandType.StoredProcedure;
    //
    // The `cmdLocal` argument lets future multi-command shapes pass a non-default
    // local; today all callers pass `__cmd`. The `indent` is the leading whitespace
    // per line as for the other Build* helpers.
    private static void BuildCommandTextAssignment(StringBuilder sb, QueryMethodModel m, string cmdLocal, string indent)
    {
        var textValue = m.IsStoredProcedure ? m.ProcedureName : m.Sql;
        var textLiteral = SymbolDisplay.FormatLiteral(textValue, quote: true);
        sb.Append(indent).Append(cmdLocal).Append(".CommandText = ").Append(textLiteral).AppendLine(";");
        if (m.IsStoredProcedure)
        {
            sb.Append(indent).Append(cmdLocal).AppendLine(".CommandType = global::System.Data.CommandType.StoredProcedure;");
        }
    }

    // EF-style open-on-execute lifecycle: open if needed, single-command execute,
    // close-on-finally. Slot held only for ExecuteScalarAsync — minimum possible for
    // a single statement. Globally-qualified type names so emit composes regardless
    // of the consumer's `using` directives; `__`-prefixed locals avoid collision
    // with user parameter names; ConfigureAwait(false) consistently — library code.
    private static void EmitScalarInt(StringBuilder sb, QueryMethodModel m, string connectionAccess)
    {
        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);
        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async global::System.Threading.Tasks.Task<int> {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        BuildConnectionPrologue(sb, connectionAccess, ct, "        ");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        BuildCommandTextAssignment(sb, m, "__cmd", "            ");
        EmitParameterBinding(sb, m);
        sb.AppendLine($"            var __result = await __cmd.ExecuteScalarAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine("            return global::System.Convert.ToInt32(__result, global::System.Globalization.CultureInfo.InvariantCulture);");
        BuildConnectionEpilogue(sb, "        ");
        sb.AppendLine("    }");
    }

    // v0.4 Phase A.2 — [Command(Kind = NonQuery)] emit. Same open-on-execute /
    // close-on-finally lifecycle the rest of the emit shapes use, with the body
    // calling ExecuteNonQueryAsync instead of ExecuteScalarAsync / ExecuteReaderAsync.
    //
    // Two return-shape branches:
    //   * Task<int> / ValueTask<int>  — capture the rows-affected count and return it.
    //   * Task / ValueTask            — await without a return statement.
    //
    // The method signature is rendered from m.ReturnTypeDisplay so the partial
    // matches the user's declaration verbatim (matters because ValueTask shape
    // is allowed alongside Task and the partial-method binding is exact).
    private static void EmitCommandNonQuery(StringBuilder sb, QueryMethodModel m, string connectionAccess)
    {
        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);

        // HasReturnValue is set authoritatively in ClassifyEmitShape — `true` for
        // Task<int> / ValueTask<int>, `false` for Task / ValueTask. Reading the flag
        // off the model avoids string-sniffing ReturnTypeDisplay (the prior
        // Contains('<') heuristic was correct but brittle).
        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        BuildConnectionPrologue(sb, connectionAccess, ct, "        ");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        BuildCommandTextAssignment(sb, m, "__cmd", "            ");
        EmitParameterBinding(sb, m);
        if (m.HasReturnValue)
        {
            sb.AppendLine($"            return await __cmd.ExecuteNonQueryAsync({ct}).ConfigureAwait(false);");
        }
        else
        {
            sb.AppendLine($"            await __cmd.ExecuteNonQueryAsync({ct}).ConfigureAwait(false);");
        }
        BuildConnectionEpilogue(sb, "        ");
        sb.AppendLine("    }");
    }

    // v0.4 Phase B.1 — [Command(Kind = Scalar)] emit. Open/execute/close lifecycle
    // around ExecuteScalarAsync. Materialization for the returned `object?` follows
    // the ConventionDiscovery model captured on m.Materialization:
    //
    //   * Primitive             — null-guard then Convert.ToXxx funnel. The
    //                              non-nullable branch THROWS InvalidOperationException
    //                              on a null `__result` (empty result set or a NULL
    //                              column) because Convert.ToInt32(null, ic) silently
    //                              returns 0 — a data-corruption hazard for callers
    //                              expecting an actual count/sum. Convert.ToXxx still
    //                              throws InvalidCastException on DBNull.Value, so the
    //                              guard only needs to handle pure `null`.
    //   * Nullable primitive    — `if (__result is null or DBNull) return null;`
    //                              followed by the typed Convert.ToXxx cast.
    //   * ValueObject / SingleArgCtor / StaticFactory
    //                           — wrap the unwrapped primitive cast in the factory
    //                              call (`new OrderId(Convert.ToInt32(__result!, ic))`).
    //   * Enum / EnumAsString   — cast to the enum's CLR type / Enum.Parse<T>(...).
    //
    // The single-column ColumnBinding carried on the MaterializationModel encodes
    // the underlying-primitive reader / factory wiring so this emit reuses the
    // exact same convention plumbing the row-shape emitters use; the only
    // difference is the inbound expression — `Convert.ToXxx(__result!, ic)`
    // instead of `__reader.GetXxx(N)`.
    private static void EmitCommandScalar(StringBuilder sb, QueryMethodModel m, string connectionAccess)
    {
        // Scalar's null-guard message names the `Task<T?>` escape hatch because
        // the nullable variant exists. EmitScalarMaterialization handles both
        // nullable and non-nullable branches based on the column binding.
        EmitScalarMaterialization(sb, m, connectionAccess,
            shapeLabelForError: "Scalar",
            nullGuardMessage: "Scalar command returned no value; use Task<T?> if null is legal.");
    }

    // v0.4 Phase C.1 — [Command(Kind = Identity)] emit. Structurally identical to
    // EmitCommandScalar's non-nullable branch: open/execute/close around
    // ExecuteScalarAsync followed by a Convert.ToXxx + optional VO factory wrap.
    // The two differences relative to Scalar:
    //   * Identity is never nullable — ClassifyCommandIdentity rejects Task<T?>
    //     so the IsNullable=true path of EmitScalarMaterialization is unreachable
    //     here; the helper still routes the non-nullable shape uniformly.
    //   * The null-guard message references "Identity" + the RETURNING /
    //     SCOPE_IDENTITY() contract rather than offering a `Task<T?>` escape.
    // Sharing the helper keeps the two emit paths in lock-step — any future
    // tweak to the connection-lifecycle, parameter binding, or materialization
    // funnel applies to both shapes without drift.
    private static void EmitCommandIdentity(StringBuilder sb, QueryMethodModel m, string connectionAccess)
    {
        EmitScalarMaterialization(sb, m, connectionAccess,
            shapeLabelForError: "CommandIdentity",
            nullGuardMessage: "Identity command returned no value; the SQL must include a RETURNING / SCOPE_IDENTITY() clause that produces a non-null value.");
    }

    // Shared materialization helper for the two ExecuteScalarAsync-based command
    // shapes (Scalar and Identity). Renders:
    //   1. The partial signature + connection prologue.
    //   2. `await using var __cmd = ...` + CommandText + parameter binding.
    //   3. `var __result = await __cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);`
    //   4. Null guard — `if (__result is null or DBNull) return null;` for the
    //      nullable Scalar branch, `if (__result is null) throw ...` otherwise.
    //   5. Convert.ToXxx / factory-wrap return expression via
    //      BuildScalarConvertExpression.
    //   6. Connection epilogue.
    //
    // v0.4 Phase E.2/E.3 — [StoredProcedure] tuple-return with output parameters.
    // Two sub-shapes resolved at emit time off
    // SprocOutputParamsMaterializationModel.ResultElements:
    //
    //   * Result + output (E.2): ResultElements.Length >= 1. Body opens the
    //     reader, materializes each result-position element by walking through
    //     ReadAsync / NextResultAsync (shared EmitMultiResultElement logic),
    //     drains the remaining rows + result sets INSIDE the `await using`
    //     scope, then disposes the reader. Parameter.Value is read back only
    //     after the reader closes — most providers (SqlClient, Npgsql,
    //     Microsoft.Data.Sqlite) only populate output parameters after the
    //     reader is fully consumed and disposed.
    //
    //   * Output-only (E.3): ResultElements.Length == 0. Body calls
    //     ExecuteNonQueryAsync; no reader is opened. Parameter.Value reads
    //     happen immediately after the await.
    //
    // Both sub-shapes use:
    //   * BuildConnectionPrologue/Epilogue for the EF-style ref-counted lifecycle.
    //   * BuildCommandTextAssignment for the CommandText + CommandType = StoredProcedure block.
    //   * EmitSprocOutputParamsBinding for the parameter binding, which sets
    //     Direction = Output on the matching positions and captures every `__p_*`
    //     local in scope so the readback can reference them.
    //   * BuildSprocOutputReadbackExpression for the `.Value` -> typed value
    //     conversion (mirrors BuildScalarConvertExpression's Convert.ToXxx funnel
    //     and wraps the result in a VO factory call when the tuple element type
    //     is a value-object / single-arg-ctor / enum).
    //
    // The detection sentinel (`// EmitShape.SprocWithOutputParams`) is rendered
    // by EmitRepository's switch case so detection tests can assert reach without
    // depending on the full body.
    private static void EmitSprocWithOutputParams(StringBuilder sb, QueryMethodModel m, string connectionAccess)
    {
        var mat = m.SprocOutputParamsMaterialization;
        if (mat is null)
        {
            // Defensive — classification should never assign SprocWithOutputParams
            // without a model. Emit the partial method body as a throw so the
            // missing wiring is visible at first invocation rather than at compile.
            var paramListFallback = BuildParameterList(m.MethodParameters);
            sb.AppendLine($"    {GeneratedCodeAttribute}");
            sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial {m.ReturnTypeDisplay} {m.MethodName}({paramListFallback})");
            sb.AppendLine($"        => throw new global::System.InvalidOperationException(\"ZeroAlloc.ORM generator invariant: SprocWithOutputParams missing SprocOutputParamsMaterialization for '{m.MethodName}'.\");");
            return;
        }

        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);
        // Build a case-insensitive set of parameter names that map to OUTPUT
        // tuple positions so the binding emit can flip Direction = Output on
        // exactly those positions. Lookup is case-insensitive to mirror the
        // tuple-field <-> parameter pairing rule that drove classification.
        var outputParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in mat.OutputElements)
            outputParamNames.Add(op.MatchingParameterName);

        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        BuildConnectionPrologue(sb, connectionAccess, ct, "        ");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        BuildCommandTextAssignment(sb, m, "__cmd", "            ");
        EmitParameterBindingWithIndent(sb, m, "            ", outputParamNames);

        var hasResultSets = mat.ResultElements.Length > 0;
        if (hasResultSets)
        {
            // Pre-declare each result-position local OUTSIDE the using scope so
            // the final `return (...)` (which lives outside the using block) can
            // reference them. The using block assigns these locals via a
            // bare-assignment variant of EmitMultiResultElement; the
            // `EmitSprocResultElements` helper drives both halves.
            EmitSprocResultElementDeclarations(sb, mat.ResultElements, indent: "            ");

            // E.2 — scoped reader so dispose happens before parameter readback.
            // The drain loops (while-ReadAsync and while-NextResultAsync) live
            // INSIDE the `await using` block — they force the provider to
            // consume the entire response so the parameter collection is
            // populated by the time the reader's DisposeAsync runs.
            sb.AppendLine($"            await using (var __reader = await __cmd.ExecuteReaderAsync({ct}).ConfigureAwait(false))");
            sb.AppendLine("            {");
            // Assign the pre-declared __elem<i> locals. returnsNullable is
            // hard-wired to false — TryBuildSprocOutputParamsMaterialization
            // rejects a nullable-outer tuple via ZAO022 (empty-first-set with
            // output parameters has unclear semantics). Shared with the
            // multi-result emit via EmitMultiResultElements with
            // declareLocal: false (the locals are pre-declared outside the
            // await-using block) and emitReturnTuple: false (the final tuple
            // construction happens after the parameter readback).
            EmitMultiResultElements(
                sb,
                mat.ResultElements,
                returnsNullable: false,
                ct,
                indent: "                ",
                declareLocal: false,
                emitReturnTuple: false);

            // Drain remaining rows + result sets so output parameters get
            // populated by every major provider. The inner `while ReadAsync`
            // consumes the trailing rows of the last expected set; the outer
            // `while NextResultAsync` then consumes any unexpected extra sets
            // the procedure produced (defensive — providers tolerate either
            // shape but only populate output params once the reader is
            // logically at end-of-stream).
            sb.AppendLine($"                while (await __reader.ReadAsync({ct}).ConfigureAwait(false)) {{ }}");
            sb.AppendLine($"                while (await __reader.NextResultAsync({ct}).ConfigureAwait(false))");
            sb.AppendLine("                {");
            sb.AppendLine($"                    while (await __reader.ReadAsync({ct}).ConfigureAwait(false)) {{ }}");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
        }
        else
        {
            // E.3 — output-only sproc; no result set. ExecuteNonQueryAsync
            // returns the rows-affected count which we discard; the only
            // values of interest live on the parameter collection.
            sb.AppendLine($"            await __cmd.ExecuteNonQueryAsync({ct}).ConfigureAwait(false);");
        }

        // Parameter readback — one local per OUTPUT element, populated from
        // the captured `__p_<param>.Value`. Convention-aware: primitives funnel
        // through BuildScalarConvertExpression, VO / single-arg-ctor wrap in
        // the factory call, enums apply the int / string cast.
        //
        // Nullable outputs declare the local with an explicit `T?` type because
        // the readback expression is a ternary that returns `null` on DBNull;
        // `var` would infer `object` and break the final tuple's positional type.
        // Non-nullable outputs keep `var` for snapshot stability with the pre-Fix-1
        // emit shape.
        var outputs = mat.OutputElements;
        for (var i = 0; i < outputs.Length; i++)
        {
            var op = outputs[i];
            var local = "__out_" + op.TupleFieldName;
            var paramLocal = "__p_" + op.MatchingParameterName;
            var expr = BuildSprocOutputReadbackExpression(op, paramLocal);
            if (op.IsNullable)
            {
                sb.AppendLine($"            {op.TypeName}? {local} = {expr};");
            }
            else
            {
                sb.AppendLine($"            var {local} = {expr};");
            }
        }

        // Final tuple construction — walk TupleElementOrder so the return
        // expression matches the user's tuple declaration verbatim regardless
        // of whether output positions came before or after result positions.
        sb.Append("            return (");
        var order = mat.TupleElementOrder;
        for (var i = 0; i < order.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            var slot = order[i];
            if (slot.Kind == SprocTupleSlotKind.Output)
            {
                var op = outputs[slot.IndexWithinKind];
                sb.Append("__out_").Append(op.TupleFieldName);
            }
            else
            {
                sb.Append("__elem").Append(slot.IndexWithinKind.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        sb.AppendLine(");");

        BuildConnectionEpilogue(sb, "        ");
        sb.AppendLine("    }");
    }

    // Pre-declare each result-position tuple local OUTSIDE the using block so
    // the final return tuple (which lives after the using's closing brace) can
    // reference them. Declared with `default!` so the locals are unambiguously
    // typed; the using-block body assigns them via plain assignment (no `var`).
    //
    // Type rendering per element kind:
    //   * Row    — `el.ElementTypeName` (reference type).
    //   * List   — `global::System.Collections.Generic.List<el.ElementTypeName>`
    //              (reference type).
    //   * Scalar — `el.ElementTypeName` plus `?` when IsNullable.
    //
    // All locals are initialized with `default!`. The bang suppresses the
    // nullable-reference warning for reference types and is a no-op for value
    // types (where `default!` is identical to `default` — the suppression
    // simply doesn't apply). Uniform `default!` keeps the emit shape simple
    // across all three kinds; the alternative of splitting value-vs-reference
    // by kind would add branching with no functional benefit.
    private static void EmitSprocResultElementDeclarations(
        StringBuilder sb,
        EquatableArray<MultiResultElement> elements,
        string indent)
    {
        for (var i = 0; i < elements.Length; i++)
        {
            var el = elements[i];
            var local = "__elem" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string typeDisplay;
            switch (el.Kind)
            {
                case MultiResultElementKind.Row:
                    typeDisplay = el.ElementTypeName;
                    break;
                case MultiResultElementKind.List:
                    typeDisplay = $"global::System.Collections.Generic.List<{el.ElementTypeName}>";
                    break;
                case MultiResultElementKind.Scalar:
                    typeDisplay = el.IsNullable ? el.ElementTypeName + "?" : el.ElementTypeName;
                    break;
                default:
                    typeDisplay = el.ElementTypeName;
                    break;
            }
            // Pre-declare result-position locals so the final return tuple can
            // reference them after the `await using (var __reader = ...)` block
            // disposes. The using-block body fills them via plain assignment
            // (no `var`) — see EmitMultiResultElement(..., declareLocal: false).
            sb.AppendLine($"{indent}{typeDisplay} {local} = default!;");
        }
    }

    // Build the expression that converts a DbParameter's boxed `.Value` into
    // the tuple-element CLR type. Mirrors BuildScalarConvertExpression's funnel
    // (Convert.ToXxx with InvariantCulture for the wide-numeric tolerance) and
    // wraps the result in a VO factory call when the element type carries a
    // Convention.
    //
    // Nullable-output handling (v0.4 Phase E review Fix 1 / Fix 11):
    //   * `op.IsNullable == true`  — the tuple element is declared `int?` /
    //                                 `string?` etc. We emit a DBNull guard so
    //                                 the readback returns `null` instead of
    //                                 throwing InvalidCastException when the
    //                                 procedure leaves the output unassigned.
    //   * `op.IsNullable == false` — the adopter has declared a non-nullable
    //                                 element; the cast/Convert.ToXxx funnel
    //                                 INTENTIONALLY throws InvalidCastException
    //                                 on DBNull. This matches the documented
    //                                 contract: declare `int?` if NULL is a
    //                                 legal value for the output position;
    //                                 otherwise treat DBNull as a procedure
    //                                 contract violation. Symmetric with the
    //                                 scalar-materialization path's null handling.
    //
    // The `.Value!` bang suppresses the nullable-reference warning on the boxed
    // accessor; null-vs-DBNull is handled at the expression level above.
    private static string BuildSprocOutputReadbackExpression(SprocOutputParam op, string paramLocal)
    {
        var subject = paramLocal + ".Value!";
        string nonNullExpr;
        if (op.Convention is { } conv && conv.FactoryFullName is not null)
        {
            // Convention wrap. Enum / EnumAsString carry their own funnel
            // (cast / Enum.Parse); VO / SingleArgCtor / StaticFactory wrap a
            // Convert.ToXxx-funnelled inner expression on the underlying primitive.
            var underlyingType = PrimitiveCatalog.GetScalarCastTypeFromReader(conv.UnderlyingReader);
            var innerExpr = BuildScalarConvertExpression(underlyingType, subject);
            nonNullExpr = conv.Kind switch
            {
                (int)ConventionKind.Enum
                    => $"({conv.FactoryFullName}){innerExpr}",
                (int)ConventionKind.EnumAsString
                    => $"global::System.Enum.Parse<{conv.FactoryFullName}>({BuildScalarConvertExpression("string", subject)})",
                _ => conv.FactoryIsCtor
                    ? $"new {conv.FactoryFullName}({innerExpr})"
                    : $"{conv.FactoryFullName}({innerExpr})",
            };
        }
        else
        {
            // Bare primitive — Convert.ToXxx funnel for width tolerance.
            nonNullExpr = BuildScalarConvertExpression(op.TypeName, subject);
        }

        if (op.IsNullable)
        {
            // Branch on DBNull so the readback short-circuits to null instead of
            // funnelling through Convert.ToXxx (which throws InvalidCastException
            // on DBNull). The local is typed `T?` upstream so the literal `null`
            // assigns cleanly for both value and reference element types.
            return $"{paramLocal}.Value is global::System.DBNull ? null : {nonNullExpr}";
        }
        return nonNullExpr;
    }

    // `shapeLabelForError` appears only in the defensive comment when the
    // model carries no Materialization (classification bug). `nullGuardMessage`
    // is the literal string embedded in the InvalidOperationException — Scalar
    // points users at `Task<T?>` for the nullable escape, Identity points at
    // the RETURNING / SCOPE_IDENTITY() contract since Identity has no nullable
    // variant.
    private static void EmitScalarMaterialization(
        StringBuilder sb,
        QueryMethodModel m,
        string connectionAccess,
        string shapeLabelForError,
        string nullGuardMessage)
    {
        var mat = m.Materialization;
        if (mat is null || mat.Columns.Length != 1)
        {
            // Defensive — classification should never assign Scalar/Identity
            // without a single-column model. Emit a comment so the missing
            // wiring is visible in the generated source.
            sb.AppendLine($"    // TODO: {shapeLabelForError} without single-column Materialization for {m.MethodName}");
            return;
        }

        var col = mat.Columns[0];
        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);

        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        BuildConnectionPrologue(sb, connectionAccess, ct, "        ");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        BuildCommandTextAssignment(sb, m, "__cmd", "            ");
        EmitParameterBindingWithIndent(sb, m, "            ");
        sb.AppendLine($"            var __result = await __cmd.ExecuteScalarAsync({ct}).ConfigureAwait(false);");

        // Nullable path emits a DBNull/null guard before the typed cast; the
        // non-nullable path STILL guards against pure `null` because
        // Convert.ToInt32(null, ic) returns 0 per .NET docs — silently returning
        // a sentinel zero for an empty COUNT result would corrupt caller logic.
        // Convert.ToXxx still throws InvalidCastException on DBNull.Value, so
        // the non-nullable branch only needs the `null` guard (DBNull funnels
        // through Convert.ToXxx and throws).
        if (col.IsNullable)
        {
            sb.AppendLine("            if (__result is null or global::System.DBNull) return null;");
        }
        else
        {
            sb.AppendLine("            if (__result is null)");
            sb.AppendLine($"                throw new global::System.InvalidOperationException(\"{nullGuardMessage}\");");
        }

        // Build the materialization expression. Provider-returned scalar types
        // are not always exact-CLR-match (e.g. Sqlite returns System.Int64 for
        // COUNT(*) and SUM, even when the C# return type is `int` / `decimal`).
        // A direct `(int)__result` cast trips InvalidCastException at runtime
        // because reference-typed unboxing is exact. We route through
        // System.Convert.ToXxx where available — same pattern the existing
        // EmitScalarInt uses — so int/long/short/byte/bool/decimal/double/
        // float/string/DateTime are tolerant of width promotion. For types
        // without a Convert.ToXxx (Guid, byte[], DateTimeOffset, TimeSpan) we
        // fall back to a direct cast; those rarely surface as widened types
        // from providers anyway.
        var bang = col.IsNullable ? "" : "!";
        string materialized;
        if (col.Convention is { } conv && conv.FactoryFullName is not null)
        {
            // For factory shapes the inner expression unwraps `__result` to the
            // factory's expected primitive — Convert.ToXxx when available so the
            // wrapping conversion handles the same provider-widening cases as
            // the bare-primitive branch below. EnumAsString unwraps to string
            // through the SAME Convert.ToString funnel so all scalar branches
            // converge on one conversion machinery.
            var underlyingType = PrimitiveCatalog.GetScalarCastTypeFromReader(conv.UnderlyingReader);
            var innerExpr = BuildScalarConvertExpression(underlyingType, "__result" + bang);
            materialized = conv.Kind switch
            {
                (int)ConventionKind.Enum
                    => $"({conv.FactoryFullName}){innerExpr}",
                // AOT note: Enum.Parse<T> is [RequiresUnreferencedCode] but safe
                // for closed enum types. Mirrors EmitFlatRow's handling.
                // BuildScalarConvertExpression("string", ...) returns
                // `Convert.ToString(__result!, ic)!` — the trailing `!` ensures
                // Enum.Parse<T> sees a non-null string.
                (int)ConventionKind.EnumAsString
                    => $"global::System.Enum.Parse<{conv.FactoryFullName}>({BuildScalarConvertExpression("string", "__result" + bang)})",
                _ => conv.FactoryIsCtor
                    ? $"new {conv.FactoryFullName}({innerExpr})"
                    : $"{conv.FactoryFullName}({innerExpr})",
            };
        }
        else
        {
            // Primitive: route through Convert.ToXxx with InvariantCulture for
            // width-tolerant conversion. col.TypeName carries the UNWRAPPED type
            // display (the model stores the cast target separately from the
            // nullable bit, so we don't have to strip a trailing `?` here).
            materialized = BuildScalarConvertExpression(col.TypeName, "__result" + bang);
        }

        sb.AppendLine($"            return {materialized};");
        BuildConnectionEpilogue(sb, "        ");
        sb.AppendLine("    }");
    }

    // Build the expression that converts a boxed `object` scalar (the result of
    // ExecuteScalarAsync) to the target primitive type. ADO.NET providers are
    // free to widen / narrow numeric scalars — Sqlite returns Int64 for any
    // integer aggregate, MS-SQL returns Int32 most of the time but Int64 for
    // BIGINT, SUM over decimal columns may surface as either decimal or double
    // depending on the driver. System.Convert.ToXxx absorbs all those
    // permutations through IConvertible without surprising the user; the
    // existing EmitScalarInt path uses the same pattern. For types lacking a
    // Convert.ToXxx (Guid, byte[], DateTimeOffset, TimeSpan, custom strings
    // outside the table) we fall back to a direct cast — those are exact-typed
    // by providers in practice.
    //
    // `subject` is the expression evaluating to the boxed value (already
    // post-bang where appropriate). The returned expression is suitable to use
    // as a `return` value or as a constructor argument.
    private static string BuildScalarConvertExpression(string targetType, string subject)
    {
        return targetType switch
        {
            "int" => $"global::System.Convert.ToInt32({subject}, global::System.Globalization.CultureInfo.InvariantCulture)",
            "long" => $"global::System.Convert.ToInt64({subject}, global::System.Globalization.CultureInfo.InvariantCulture)",
            "short" => $"global::System.Convert.ToInt16({subject}, global::System.Globalization.CultureInfo.InvariantCulture)",
            "byte" => $"global::System.Convert.ToByte({subject}, global::System.Globalization.CultureInfo.InvariantCulture)",
            "bool" => $"global::System.Convert.ToBoolean({subject}, global::System.Globalization.CultureInfo.InvariantCulture)",
            "decimal" => $"global::System.Convert.ToDecimal({subject}, global::System.Globalization.CultureInfo.InvariantCulture)",
            "double" => $"global::System.Convert.ToDouble({subject}, global::System.Globalization.CultureInfo.InvariantCulture)",
            "float" => $"global::System.Convert.ToSingle({subject}, global::System.Globalization.CultureInfo.InvariantCulture)",
            "string" => $"global::System.Convert.ToString({subject}, global::System.Globalization.CultureInfo.InvariantCulture)!",
            "global::System.DateTime" => $"global::System.Convert.ToDateTime({subject}, global::System.Globalization.CultureInfo.InvariantCulture)",
            // No Convert.ToXxx exists for Guid / byte[] / DateTimeOffset /
            // TimeSpan — fall back to a direct cast. Providers return these as
            // exact CLR types in practice (Sqlite TimeSpan via Microsoft.Data.Sqlite,
            // Npgsql DateTimeOffset, etc.).
            _ => $"({targetType}){subject}",
        };
    }

    // Single-row scalar with null tolerance — distinguishes three cases:
    //   * empty result set         -> null
    //   * first row, NULL column   -> null
    //   * first row, value         -> the typed value
    // Uses ExecuteReaderAsync + ReadAsync/IsDBNull/GetXxx rather than
    // ExecuteScalarAsync because the latter conflates empty-set and NULL.
    // The method signature is rendered from m.ReturnTypeDisplay so the partial
    // matches the user declaration's nullable annotation verbatim.
    private static void EmitNullableScalar(StringBuilder sb, QueryMethodModel m, string connectionAccess)
    {
        var readerMethod = m.NullableScalarReaderMethod ?? "GetValue";
        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);
        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        BuildConnectionPrologue(sb, connectionAccess, ct, "        ");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        BuildCommandTextAssignment(sb, m, "__cmd", "            ");
        EmitParameterBinding(sb, m);
        sb.AppendLine($"            await using var __reader = await __cmd.ExecuteReaderAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine($"            if (!await __reader.ReadAsync({ct}).ConfigureAwait(false))");
        sb.AppendLine("                return null;");
        sb.AppendLine($"            return __reader.IsDBNull(0) ? null : __reader.{readerMethod}(0);");
        BuildConnectionEpilogue(sb, "        ");
        sb.AppendLine("    }");
    }

    // Single-row positional-record materialization. Each ctor parameter binds to the
    // reader column at the same ordinal — purely positional because the v0.1 surface
    // requires the SELECT column order to match the record ctor order. Column-name
    // matching lands in v0.2.
    //
    // For nullable columns we wrap the GetXxx call in an IsDBNull(N) guard so the
    // ctor receives `null` instead of throwing on DBNull. Non-nullable columns are
    // read directly — if the DB returns NULL there it's a schema/SQL bug surfaced
    // as an InvalidCastException at runtime.
    //
    // No-row case returns null because this shape only triggers for Task<T?>.
    // Parameter binding (e.g. @id <- the `int id` arg) lands in Phase 6; for now
    // the SQL is sent as-is, which means unbound placeholders read as NULL.
    private static void EmitFlatRow(StringBuilder sb, QueryMethodModel m, string connectionAccess)
    {
        var mat = m.Materialization;
        if (mat is null)
        {
            // Defensive — classification should never assign FlatRow without a model.
            sb.AppendLine($"    // TODO: FlatRow without Materialization model for {m.MethodName}");
            return;
        }

        // v0.5 Phase A — flattened column count accounts for nested composites. The
        // OUTER ctor arity may be < flattened-column count when one or more ctor
        // params are MultiArgCtor composites (each expanding to N inner reads).
        var flattenedColumnCount = ComputeFlattenedColumnCount(mat.Columns);
        var hasNestedComposite = flattenedColumnCount > mat.Columns.Length;

        // v0.5 Phase C — when any nested composite is nullable (`Money? Total`),
        // the inline `new T(..., new Money(...), ...)` construction can't host
        // the all-or-nothing branching inside an expression position. The
        // hoisted-local pattern evaluates each nullable composite's inner-DBNull
        // check + materialize-or-throw above the outer `new T(...)` call, then
        // references the locals as ctor arguments. Non-nullable composites stay
        // on the inline path so existing snapshots remain byte-identical.
        var hasNullableComposite = HasNullableCompositeColumn(mat.Columns);

        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);
        // v0.5 Phase A (post-review Fix 1) — sentinel sits BEFORE the
        // [GeneratedCode] attribute for parity with the v0.4 SprocWithOutputParams
        // sentinel and the v0.5 composite-scalar sentinel.
        if (hasNullableComposite)
        {
            sb.AppendLine($"    // EmitShape: FlatRow with nullable nested composite (flattened columns: {flattenedColumnCount.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        }
        else if (hasNestedComposite)
        {
            sb.AppendLine($"    // EmitShape: FlatRow with nested composite (flattened columns: {flattenedColumnCount.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        }
        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        BuildConnectionPrologue(sb, connectionAccess, ct, "        ");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        BuildCommandTextAssignment(sb, m, "__cmd", "            ");
        EmitParameterBinding(sb, m);
        sb.AppendLine($"            await using var __reader = await __cmd.ExecuteReaderAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine($"            if (!await __reader.ReadAsync({ct}).ConfigureAwait(false))");
        sb.AppendLine("                return null;");

        if (hasNullableComposite)
        {
            EmitFlatRowWithHoistedLocals(sb, mat, useNamedOrdinals: false);
        }
        else
        {
            sb.AppendLine($"            return new {mat.TargetTypeFullName}(");
            var cols = mat.Columns;
            var ordinal = 0;
            for (var i = 0; i < cols.Length; i++)
            {
                var col = cols[i];
                var trailing = i == cols.Length - 1 ? ");" : ",";
                // v0.5 Phase A — composite ctor parameter: expand into a nested
                // `new T(reader.GetXxx(ord), ...)` call spanning N consecutive
                // ordinals. The outer FlatRow's ordinal cursor advances by N for
                // the composite; following columns continue from ord+N.
                if (col.InnerColumns.Length > 0)
                {
                    EmitNestedCompositeConstruction(sb, col, ordinal, "                ", trailing);
                    ordinal += col.InnerColumns.Length;
                    continue;
                }
                var expr = BuildPositionalReadExpression(col, ordinal);
                sb.AppendLine($"                {expr}{trailing}");
                ordinal++;
            }
        }
        BuildConnectionEpilogue(sb, "        ");
        sb.AppendLine("    }");
    }

    // v0.5 Phase C — does any column in this materialization carry a NULLABLE
    // composite ctor parameter? Only composite bindings can be nullable in a
    // way that affects emit (the C# nullable annotation on a non-composite
    // primitive/VO column is already handled by the per-column IsDBNull guard
    // baked into BuildPositionalReadExpression). The hoisted-local FlatRow
    // emit branches on this so existing snapshots for non-nullable-composite
    // shapes don't drift.
    private static bool HasNullableCompositeColumn(EquatableArray<ColumnBinding> columns)
    {
        foreach (var col in columns)
        {
            if (col.InnerColumns.Length > 0 && col.IsNullable) return true;
        }
        return false;
    }

    // v0.5 Phase C — emit the FlatRow / DomainEntity body using hoisted
    // locals when one or more nested composites are nullable. The shape:
    //
    //   var __<param>_ord_0 = ... (only for non-composite columns; nullable
    //                              composites read each inner-IsDBNull
    //                              positionally inside the hoisted block)
    //   var __<compositeName> = (Money?)(__a_isNull && __b_isNull ? null
    //                              : ...);
    //   return new OuterT(<__col0>, <__compositeLocal>, <__col2>, ...);
    //
    // Each ctor argument position renders to a value-expression that the
    // outer `new T(...)` consumes verbatim. Non-composite columns inline as
    // their positional/named read expression; non-nullable composite columns
    // inline as the existing nested `new TComposite(...)` expression;
    // nullable composite columns become hoisted-local references to a
    // pre-computed value.
    private static void EmitFlatRowWithHoistedLocals(
        StringBuilder sb,
        MaterializationModel mat,
        bool useNamedOrdinals)
    {
        var cols = mat.Columns;
        // Pass 1 — emit hoisted locals for nullable composites in the order
        // they appear. The ordinal cursor mirrors EmitFlatRow's positional
        // pass; composite columns consume N consecutive positions even when
        // hoisted, and non-nullable / non-composite columns still occupy one
        // position each.
        var hoistedNames = new string?[cols.Length];
        var ordinal = 0;
        for (var i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            if (col.InnerColumns.Length > 0 && col.IsNullable)
            {
                // Post-review Fix 6 — prefer the outer ctor-arg name
                // (e.g. `__Total` for `record OrderRow(int Id, Money? Total)`)
                // over the positional `__composite_<i>`. CtorArgName is set on
                // composite ColumnBindings by TryBuildCompositeColumnBinding.
                // The `__composite_<i>` fallback only fires for legacy
                // bindings that pre-date Fix 6.
                //
                // Post-review Fix 7 — initialize the hoisted local with
                // `default!` so a future refactor that takes a non-exhaustive
                // branch out of EmitNullableCompositeHoistedBlock can't trip
                // CS0165 "use of unassigned local". The `!` suppresses the
                // CS8600 null-tolerance warning for nullable-reference
                // composite types in adopter code that has `<Nullable>enable`.
                var localSuffix = col.CtorArgName ?? "composite_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var localName = "__" + localSuffix;
                hoistedNames[i] = localName;
                sb.AppendLine($"            {col.TypeName}? {localName} = default!;");
                sb.AppendLine("            {");
                EmitNullableCompositeHoistedBlock(sb, col, ordinal, "                ", useNamedOrdinals, localName);
                sb.AppendLine("            }");
                ordinal += col.InnerColumns.Length;
            }
            else if (col.InnerColumns.Length > 0)
            {
                ordinal += col.InnerColumns.Length;
            }
            else
            {
                ordinal += 1;
            }
        }

        // Pass 2 — render the `return new T(...)` with each column position
        // resolved to either the hoisted local or the inline read expression.
        sb.AppendLine($"            return new {mat.TargetTypeFullName}(");
        ordinal = 0;
        for (var i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            var trailing = i == cols.Length - 1 ? ");" : ",";
            if (hoistedNames[i] is { } hoisted)
            {
                sb.AppendLine($"                {hoisted}{trailing}");
                ordinal += col.InnerColumns.Length;
            }
            else if (col.InnerColumns.Length > 0)
            {
                if (useNamedOrdinals)
                {
                    EmitNestedCompositeConstructionByOrdinalName(sb, col, "                ", trailing);
                }
                else
                {
                    EmitNestedCompositeConstruction(sb, col, ordinal, "                ", trailing);
                }
                ordinal += col.InnerColumns.Length;
            }
            else
            {
                var expr = useNamedOrdinals
                    ? BuildOrdinalNameReadExpression(col)
                    : BuildPositionalReadExpression(col, ordinal);
                sb.AppendLine($"                {expr}{trailing}");
                ordinal += 1;
            }
        }
    }

    // v0.5 Phase C — emit the all-or-nothing DBNull check + materialize body
    // for a single nullable composite column inside a hoisted-local block.
    // Differs from EmitNullableCompositeAllOrNothing (scalar-position) in
    // two ways:
    //
    //   * Inner-column IsDBNull / Get reads are POSITIONAL when the outer
    //     row uses positional ordinals (FlatRow). The base ordinal is the
    //     composite's starting column index in the outer SELECT list, not
    //     0. For DomainEntity (useNamedOrdinals: true) the inner reads use
    //     GetOrdinal(<innerColumnName>) so the base ordinal is irrelevant
    //     and inner ColumnName must be populated upstream.
    //
    //   * The body assigns to a pre-declared local instead of `return`-ing.
    //     The mixed-null branch still throws (escaping the assignment).
    private static void EmitNullableCompositeHoistedBlock(
        StringBuilder sb,
        ColumnBinding composite,
        int baseOrdinal,
        string indent,
        bool useNamedOrdinals,
        string localName)
    {
        var inner = composite.InnerColumns;
        var nullLocalNames = new string[inner.Length];
        // Hoist per-column ordinal locals first when we're in the named path so
        // GetOrdinal(<name>) runs exactly once per inner column across the
        // IsDBNull + materialize reads. Positional reads use the integer
        // literal directly — no hoisting saves anything there.
        //
        // Post-review Fix 2 — the per-inner-column `baseName` falls back to
        // CtorArgName before the positional `col<j>`. ColumnName is set on
        // DomainEntity inner bindings; CtorArgName is set on every composite
        // inner binding (Fix 2). The `col<j>` sentinel is only reachable for
        // legacy bindings that pre-date Fix 2.
        var ordinalLocalNames = new string?[inner.Length];
        if (useNamedOrdinals)
        {
            for (var j = 0; j < inner.Length; j++)
            {
                var b = inner[j];
                var baseName = b.ColumnName ?? b.CtorArgName ?? "col" + j.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var ordLocal = "__" + baseName + "_ord";
                ordinalLocalNames[j] = ordLocal;
                var literal = SymbolDisplay.FormatLiteral(b.ColumnName ?? string.Empty, quote: true);
                sb.AppendLine($"{indent}var {ordLocal} = __reader.GetOrdinal({literal});");
            }
        }
        for (var j = 0; j < inner.Length; j++)
        {
            var b = inner[j];
            var baseName = b.ColumnName ?? b.CtorArgName ?? "col" + j.ToString(System.Globalization.CultureInfo.InvariantCulture);
            nullLocalNames[j] = "__" + baseName + "_isNull";
            string ordinalExpr = useNamedOrdinals
                ? ordinalLocalNames[j]!
                : (baseOrdinal + j).ToString(System.Globalization.CultureInfo.InvariantCulture);
            sb.AppendLine($"{indent}var {nullLocalNames[j]} = __reader.IsDBNull({ordinalExpr});");
        }

        var allNullExpr = string.Join(" && ", nullLocalNames);
        sb.AppendLine($"{indent}if ({allNullExpr})");
        sb.AppendLine($"{indent}    {localName} = null;");
        var anyNullExpr = string.Join(" || ", nullLocalNames);
        sb.AppendLine($"{indent}else if ({anyNullExpr})");
        var messagePrefix = "Nullable composite '" + composite.TypeName + "' has mixed-null columns: ";
        var messagePrefixLit = SymbolDisplay.FormatLiteral(messagePrefix, quote: true);
        var suffixLit = SymbolDisplay.FormatLiteral(". All-or-nothing required.", quote: true);
        sb.AppendLine($"{indent}    throw new global::ZeroAlloc.ORM.ZeroAllocOrmMaterializationException(");
        sb.Append($"{indent}        {messagePrefixLit}");
        for (var j = 0; j < inner.Length; j++)
        {
            var b = inner[j];
            var label = b.ColumnName ?? b.CtorArgName ?? "col" + j.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var labelLit = SymbolDisplay.FormatLiteral(label + ".isNull=", quote: true);
            sb.Append(" + ").Append(labelLit).Append(" + ").Append(nullLocalNames[j]);
            if (j < inner.Length - 1)
            {
                sb.Append(" + \", \"");
            }
        }
        sb.AppendLine($" + {suffixLit});");
        sb.AppendLine($"{indent}else");
        sb.AppendLine($"{indent}    {localName} = new {composite.TypeName}(");
        for (var j = 0; j < inner.Length; j++)
        {
            var b = inner[j];
            var trailing = j == inner.Length - 1 ? ");" : ",";
            string readExpr;
            if (useNamedOrdinals)
            {
                // Reuse the hoisted ordinal local instead of calling GetOrdinal
                // again — v0.3-CLN1 perf footgun avoidance per Phase C.1 plan.
                readExpr = BuildReadExpressionWithOrdinalLocal(b, ordinalLocalNames[j]!);
            }
            else
            {
                readExpr = BuildPositionalReadExpression(b, baseOrdinal + j);
            }
            sb.AppendLine($"{indent}        {readExpr}{trailing}");
        }
    }

    // v0.5 Phase C — DomainEntity-style read where the ordinal has already been
    // hoisted to a local (avoiding GetOrdinal(<name>) duplication across the
    // IsDBNull + Get reads in the nullable-composite hoisted block). Mirrors
    // BuildOrdinalNameReadExpression but substitutes an arbitrary ordinal
    // expression for the inline GetOrdinal(<literal>) call.
    private static string BuildReadExpressionWithOrdinalLocal(ColumnBinding col, string ordinalExpr)
    {
        var readExpr = $"__reader.{col.GetterMethod}({ordinalExpr})";
        if (col.Convention is { } conv && conv.FactoryFullName is not null)
        {
            readExpr = conv.Kind switch
            {
                (int)ConventionKind.Enum
                    => $"({conv.FactoryFullName}){readExpr}",
                (int)ConventionKind.EnumAsString
                    => $"global::System.Enum.Parse<{conv.FactoryFullName}>({readExpr})",
                _ => conv.FactoryIsCtor
                    ? $"new {conv.FactoryFullName}({readExpr})"
                    : $"{conv.FactoryFullName}({readExpr})",
            };
        }
        if (col.IsNullable)
        {
            return $"__reader.IsDBNull({ordinalExpr}) ? ({col.TypeName}?)null : {readExpr}";
        }
        return readExpr;
    }

    // Sum the flattened column count for a materialization's binding list. A
    // composite binding (InnerColumns non-empty) contributes its inner count;
    // leaf bindings contribute 1. Used by FlatRow / DomainEntity to thread the
    // column-index cursor across nested composites correctly.
    private static int ComputeFlattenedColumnCount(EquatableArray<ColumnBinding> columns)
    {
        var n = 0;
        foreach (var col in columns)
        {
            n += col.InnerColumns.Length > 0 ? col.InnerColumns.Length : 1;
        }
        return n;
    }

    // Emit `new T(reader.GetXxx(ord+0), reader.GetXxx(ord+1), ...)` for a composite
    // ctor parameter nested inside a FlatRow / DomainEntity. The composite's inner
    // bindings consume a contiguous ordinal range starting at `baseOrdinal`. Each
    // inner column's read expression is built via BuildPositionalReadExpression —
    // convention wrappers (VO / SingleArgCtor / StaticFactory / Enum / EnumAsString)
    // layer on top of the primitive read as in the leaf case.
    //
    // `trailing` is the outer FlatRow's per-column suffix (a comma for non-last
    // columns, `);` for the last). The composite's closing paren is merged with
    // that suffix on the same line so the generated source matches the visual
    // shape of the existing FlatRow snapshots (no orphaned `,` / `);` on its own
    // indent level).
    private static void EmitNestedCompositeConstruction(
        StringBuilder sb,
        ColumnBinding composite,
        int baseOrdinal,
        string indent,
        string trailing)
    {
        // v0.5 Phase D — when the nested composite carries a FactoryMethodName,
        // emit `T.Factory(...)` instead of `new T(...)`. The inner reads stay
        // positional. A sentinel comment marks the factory dispatch so snapshots
        // make the branch unambiguous.
        if (composite.FactoryMethodName is { } factoryName)
        {
            sb.AppendLine($"{indent}// FactoryDispatch: {composite.TypeName}.{factoryName}");
            sb.AppendLine($"{indent}{composite.TypeName}.{factoryName}(");
        }
        else
        {
            sb.AppendLine($"{indent}new {composite.TypeName}(");
        }
        var inner = composite.InnerColumns;
        for (var j = 0; j < inner.Length; j++)
        {
            var b = inner[j];
            // Last inner column: close the composite `)` AND attach the outer
            // `trailing` (`,` or `);`) so the diff stays compact. Intermediate
            // columns get a plain `,`.
            var innerTrailing = j == inner.Length - 1 ? ")" + trailing : ",";
            var readExpr = BuildPositionalReadExpression(b, baseOrdinal + j);
            sb.AppendLine($"{indent}    {readExpr}{innerTrailing}");
        }
    }

    // v0.5 Phase A — composite at scalar position emit. The user declared
    // `Task<Money> M(...)`; the SELECT list produces the composite's inner
    // columns at ordinals 0..N-1 and we construct `new Money(GetXxx(0), ...)`
    // directly. Empty result-set throws ZeroAllocOrmMaterializationException —
    // the composite return is non-nullable, so there's no `return null` escape
    // hatch.
    //
    // v0.5 Phase C — when MaterializationModel.IsNullable is true (the user
    // declared `Task<Money?>`), the emit branches into the all-or-nothing
    // pattern (design Section 3.5, line 330):
    //
    //   * Empty result-set        -> return null;
    //   * All inner columns DBNull -> return null;
    //   * Any (but not all) DBNull -> throw ZeroAllocOrmMaterializationException
    //                                  with a message naming the mixed columns;
    //   * Otherwise                -> materialize normally.
    //
    // Each inner column's IsDBNull is hoisted into a __<ctorArg>_isNull local
    // so the predicate evaluates exactly once per ordinal. The materialize
    // path reuses BuildPositionalReadExpression so convention wrappers (VO /
    // SingleArgCtor / Enum) layer on identically to the non-nullable case.
    private static void EmitComposite(StringBuilder sb, QueryMethodModel m, string connectionAccess)
    {
        var mat = m.Materialization;
        // Length < 2 invariant on the ctor path: a 1-arg ctor would have routed
        // through TrySingleArgCtor (different convention), not TryMultiArgCtor.
        // Factory dispatch (Phase D) relaxes to Length >= 1 — adopters can supply
        // a single-arg factory for primitives wrapped in a custom hook, though
        // the canonical Money.FromStorage shape carries >= 2 params.
        var minColumns = mat?.FactoryMethodName is not null ? 1 : 2;
        if (mat is null || mat.Kind != MaterializationKind.Composite || mat.Columns.Length < minColumns)
        {
            sb.AppendLine($"    // TODO: Composite without inner-column Materialization for {m.MethodName}");
            return;
        }

        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);
        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        BuildConnectionPrologue(sb, connectionAccess, ct, "        ");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        BuildCommandTextAssignment(sb, m, "__cmd", "            ");
        EmitParameterBinding(sb, m);
        sb.AppendLine($"            await using var __reader = await __cmd.ExecuteReaderAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine($"            if (!await __reader.ReadAsync({ct}).ConfigureAwait(false))");
        if (mat.IsNullable)
        {
            sb.AppendLine("                return null;");
            EmitNullableCompositeAllOrNothing(sb, mat, "            ", useNamedOrdinals: false);
        }
        else
        {
            sb.AppendLine("                throw new global::ZeroAlloc.ORM.ZeroAllocOrmMaterializationException(\"Composite scalar query returned no row.\");");
            // v0.5 Phase D — factory dispatch replaces `new T(...)` with
            // `T.FactoryMethodName(...)`. Otherwise the inner column reads
            // are identical to the ctor-call shape; convention wrappers
            // (VO / SingleArgCtor / Enum) layer on top per-column through
            // BuildPositionalReadExpression.
            var ctorOrFactory = mat.FactoryMethodName is { } fac
                ? $"{mat.TargetTypeFullName}.{fac}"
                : $"new {mat.TargetTypeFullName}";
            sb.AppendLine($"            return {ctorOrFactory}(");
            var cols = mat.Columns;
            for (var i = 0; i < cols.Length; i++)
            {
                var col = cols[i];
                var trailing = i == cols.Length - 1 ? ");" : ",";
                var readExpr = BuildPositionalReadExpression(col, i);
                sb.AppendLine($"                {readExpr}{trailing}");
            }
        }
        BuildConnectionEpilogue(sb, "        ");
        sb.AppendLine("    }");
    }

    // v0.5 Phase C — emit the all-or-nothing DBNull check + materialize-or-throw
    // body for a nullable composite at scalar position. The composite's inner
    // ColumnBindings are at ordinals 0..N-1; each gets one IsDBNull(N) call
    // hoisted into a `__<ctorArg>_isNull` local. The aggregate predicates use
    // `&&` (all-null -> return null) and `||` (any-null -> throw).
    //
    // useNamedOrdinals: when true, each inner column's IsDBNull / Get call
    // uses `__reader.GetOrdinal("<ColumnName>")` instead of a positional
    // index. Used by the DomainEntity / FlatRow nested-composite-hoisted-local
    // path; scalar-position Task<Money?> uses the positional variant.
    //
    // The mixed-null exception message names each inner column + its IsDBNull
    // state so an adopter debugging a partial-null row can see exactly which
    // column violated the contract. The composite type's display name is
    // emitted so multi-composite queries point at the right shape.
    private static void EmitNullableCompositeAllOrNothing(
        StringBuilder sb,
        MaterializationModel mat,
        string indent,
        bool useNamedOrdinals)
    {
        var cols = mat.Columns;
        // Per-column IsDBNull hoisting. The local name follows the ctor-arg
        // name when available (ColumnName is set on DomainEntity bindings)
        // and falls back to a positional `__col{i}` when ColumnName is null
        // (FlatRow positional bindings). Both shapes coexist in the same
        // method body because the inner ColumnBinding shape decides.
        var nullLocalNames = new string[cols.Length];
        // Per-column ordinal hoisting for the named-ordinal path (DomainEntity-
        // style). v0.3-CLN1 footgun: calling GetOrdinal(<name>) once per IsDBNull
        // AND once per Get*-call duplicates work; hoisting into a single local
        // shares the lookup. Positional reads skip this — the integer literal
        // is already a no-cost expression.
        var ordinalLocalNames = new string?[cols.Length];
        if (useNamedOrdinals)
        {
            for (var i = 0; i < cols.Length; i++)
            {
                var col = cols[i];
                var baseName = col.ColumnName ?? col.CtorArgName ?? "col" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var ordLocal = "__" + baseName + "_ord";
                ordinalLocalNames[i] = ordLocal;
                var literal = SymbolDisplay.FormatLiteral(col.ColumnName ?? string.Empty, quote: true);
                sb.AppendLine($"{indent}var {ordLocal} = __reader.GetOrdinal({literal});");
            }
        }
        for (var i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            // Post-review Fix 2 — fall back to CtorArgName before `col<i>` so
            // the IsDBNull local + the mixed-null exception message show the
            // composite's inner ctor-arg name (e.g. `__Amount_isNull`)
            // instead of the positional `__col0_isNull`. Inner composite
            // columns now always carry CtorArgName (Fix 2).
            var baseName = col.ColumnName ?? col.CtorArgName ?? "col" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            nullLocalNames[i] = "__" + baseName + "_isNull";
            string ordinalExpr = useNamedOrdinals
                ? ordinalLocalNames[i]!
                : i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sb.AppendLine($"{indent}var {nullLocalNames[i]} = __reader.IsDBNull({ordinalExpr});");
        }

        // All-null short-circuit -> return null.
        var allNullExpr = string.Join(" && ", nullLocalNames);
        sb.AppendLine($"{indent}if ({allNullExpr}) return null;");

        // Any-null but not all -> throw. The message lists every column + its
        // IsDBNull state so an adopter debugging a partial-null row sees the
        // exact mix. Use `string.Concat` style via `+` to keep it allocation-
        // friendly (one StringBuilder under the hood); each piece is a const
        // literal save the bool ToString.
        var anyNullExpr = string.Join(" || ", nullLocalNames);
        sb.AppendLine($"{indent}if ({anyNullExpr})");
        // Build the throw expression. Each "Field.isNull=" + bool concatenation
        // is straightforward; we line-wrap for readability in the generated
        // source by joining with `, ` literals between fields.
        var messagePrefix = "Nullable composite '" + mat.TargetTypeFullName + "' has mixed-null columns: ";
        var messagePrefixLit = SymbolDisplay.FormatLiteral(messagePrefix, quote: true);
        var suffixLit = SymbolDisplay.FormatLiteral(". All-or-nothing required.", quote: true);
        sb.AppendLine($"{indent}    throw new global::ZeroAlloc.ORM.ZeroAllocOrmMaterializationException(");
        sb.Append($"{indent}        {messagePrefixLit}");
        for (var i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            var label = col.ColumnName ?? col.CtorArgName ?? "col" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var labelLit = SymbolDisplay.FormatLiteral(label + ".isNull=", quote: true);
            sb.Append(" + ").Append(labelLit).Append(" + ").Append(nullLocalNames[i]);
            if (i < cols.Length - 1)
            {
                sb.Append(" + \", \"");
            }
        }
        sb.AppendLine($" + {suffixLit});");

        // Otherwise -> materialize normally. v0.5 Phase D — factory dispatch
        // swaps `new T(...)` for `T.Factory(...)` identically to the
        // non-nullable case (EmitComposite); inner column reads stay positional
        // or named-ordinal per the caller.
        var ctorOrFactory = mat.FactoryMethodName is { } fac
            ? $"{mat.TargetTypeFullName}.{fac}"
            : $"new {mat.TargetTypeFullName}";
        sb.AppendLine($"{indent}return {ctorOrFactory}(");
        for (var i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            var trailing = i == cols.Length - 1 ? ");" : ",";
            string readExpr = useNamedOrdinals
                ? BuildReadExpressionWithOrdinalLocal(col, ordinalLocalNames[i]!)
                : BuildPositionalReadExpression(col, i);
            sb.AppendLine($"{indent}    {readExpr}{trailing}");
        }
    }

    // Build the materialization expression for a single ColumnBinding at the given
    // positional ordinal. Convention wrappers (VO / SingleArgCtor / StaticFactory /
    // Enum / EnumAsString) are layered on top of the raw GetXxx read identically to
    // EmitFlatRow. Nullable columns wrap in a `IsDBNull(N) ? null : ...` ternary.
    // Composite ColumnBindings (InnerColumns non-empty) are NOT supported here —
    // those are flattened by the outer caller (EmitFlatRow / EmitDomainEntity).
    private static string BuildPositionalReadExpression(ColumnBinding col, int ordinal)
    {
        var readExpr = $"__reader.{col.GetterMethod}({ordinal})";
        if (col.Convention is { } conv && conv.FactoryFullName is not null)
        {
            readExpr = conv.Kind switch
            {
                (int)ConventionKind.Enum
                    => $"({conv.FactoryFullName}){readExpr}",
                (int)ConventionKind.EnumAsString
                    => $"global::System.Enum.Parse<{conv.FactoryFullName}>({readExpr})",
                _ => conv.FactoryIsCtor
                    ? $"new {conv.FactoryFullName}({readExpr})"
                    : $"{conv.FactoryFullName}({readExpr})",
            };
        }
        if (col.IsNullable)
        {
            return $"__reader.IsDBNull({ordinal}) ? ({col.TypeName}?)null : {readExpr}";
        }
        return readExpr;
    }

    // Multi-arg class materialization. Conceptually identical to EmitFlatRow but each
    // column read goes through `__reader.GetOrdinal("ColumnName")` instead of a fixed
    // positional index, so the SELECT column order is not load-bearing. Empty result
    // returns null (the shape only triggers for Task<T?> at present).
    //
    // Phase E v0.2 ships the column-name path for primitives + Phase-C conventions +
    // Phase-D enums. Anything not resolvable to a known convention bails out of
    // DomainEntity detection earlier (TryBuildDomainEntityMaterialization returns null)
    // so the emitter never sees a half-classified DomainEntity here.
    private static void EmitDomainEntity(StringBuilder sb, QueryMethodModel m, string connectionAccess)
    {
        var mat = m.Materialization;
        if (mat is null)
        {
            sb.AppendLine($"    // TODO: DomainEntity without Materialization model for {m.MethodName}");
            return;
        }

        // v0.5 Phase A — DomainEntity may contain nested composites just like FlatRow.
        // Flattened column count drives the sentinel comment; the column reads still
        // funnel through GetOrdinal(name) because DomainEntity keys columns by name.
        var flattenedColumnCount = ComputeFlattenedColumnCount(mat.Columns);
        var hasNestedComposite = flattenedColumnCount > mat.Columns.Length;
        // v0.5 Phase C — nullable nested composites route through the
        // hoisted-local emit path (see EmitFlatRowWithHoistedLocals comment
        // for the architectural rationale). DomainEntity reuses the same
        // helper with useNamedOrdinals: true so inner reads still use
        // GetOrdinal(<columnName>).
        var hasNullableComposite = HasNullableCompositeColumn(mat.Columns);

        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);
        // v0.5 Phase A (post-review Fix 1) — sentinel BEFORE attribute (see EmitFlatRow).
        if (hasNullableComposite)
        {
            sb.AppendLine($"    // EmitShape: DomainEntity with nullable nested composite (flattened columns: {flattenedColumnCount.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        }
        else if (hasNestedComposite)
        {
            sb.AppendLine($"    // EmitShape: DomainEntity with nested composite (flattened columns: {flattenedColumnCount.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        }
        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        BuildConnectionPrologue(sb, connectionAccess, ct, "        ");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        BuildCommandTextAssignment(sb, m, "__cmd", "            ");
        EmitParameterBinding(sb, m);
        sb.AppendLine($"            await using var __reader = await __cmd.ExecuteReaderAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine($"            if (!await __reader.ReadAsync({ct}).ConfigureAwait(false))");
        sb.AppendLine("                return null;");

        if (hasNullableComposite)
        {
            EmitFlatRowWithHoistedLocals(sb, mat, useNamedOrdinals: true);
        }
        else
        {
            // v0.3-CLN1 — hoist GetOrdinal(<name>) into a per-column local so the
            // IsDBNull(...) + GetXxx(...) reads share one lookup instead of two.
            // For non-nullable leaves the runtime saving is small (one call vs
            // one call), but uniform hoisting keeps the emit shape consistent
            // with the nullable-composite path (EmitFlatRowWithHoistedLocals)
            // and avoids the double call on nullable leaf columns.
            var hoistedOrdinals = EmitOrdinalHoistsForColumns(sb, mat.Columns, "            ");
            sb.AppendLine($"            return new {mat.TargetTypeFullName}(");
            var cols = mat.Columns;
            for (var i = 0; i < cols.Length; i++)
            {
                var col = cols[i];
                var trailing = i == cols.Length - 1 ? ");" : ",";
                // v0.5 Phase A — composite ctor parameter in a DomainEntity. The inner
                // ColumnBindings carry their own column names (PascalCased inner ctor
                // param names) so each inner read reuses the per-inner-column hoisted
                // ordinal local; the outer construction wraps in `new TComposite(...)`.
                if (col.InnerColumns.Length > 0)
                {
                    EmitNestedCompositeConstructionByOrdinalNameWithHoisted(sb, col, hoistedOrdinals[i]!, "                ", trailing);
                    continue;
                }
                // Column name comes from the ctor parameter (PascalCased). The hoisted
                // ordinal local feeds both the IsDBNull guard (for nullable leaves) and
                // the GetXxx read so GetOrdinal runs once per column per row.
                var expr = BuildReadExpressionWithOrdinalLocal(col, hoistedOrdinals[i]![0]);
                sb.AppendLine($"                {expr}{trailing}");
            }
        }
        BuildConnectionEpilogue(sb, "        ");
        sb.AppendLine("    }");
    }

    // Build the materialization expression for a single ColumnBinding using
    // GetOrdinal(<ColumnName>). Convention wrappers and nullable IsDBNull guards
    // layer on top identically to the positional variant.
    private static string BuildOrdinalNameReadExpression(ColumnBinding col)
    {
        var colNameLiteral = SymbolDisplay.FormatLiteral(col.ColumnName ?? string.Empty, quote: true);
        var ordinalExpr = $"__reader.GetOrdinal({colNameLiteral})";
        var readExpr = $"__reader.{col.GetterMethod}({ordinalExpr})";
        if (col.Convention is { } conv && conv.FactoryFullName is not null)
        {
            readExpr = conv.Kind switch
            {
                (int)ConventionKind.Enum
                    => $"({conv.FactoryFullName}){readExpr}",
                (int)ConventionKind.EnumAsString
                    => $"global::System.Enum.Parse<{conv.FactoryFullName}>({readExpr})",
                _ => conv.FactoryIsCtor
                    ? $"new {conv.FactoryFullName}({readExpr})"
                    : $"{conv.FactoryFullName}({readExpr})",
            };
        }
        if (col.IsNullable)
        {
            return $"__reader.IsDBNull({ordinalExpr}) ? ({col.TypeName}?)null : {readExpr}";
        }
        return readExpr;
    }

    // Emit `new T(__reader.GetOrdinal("A") and friends, ...)` for a composite ctor
    // parameter nested inside a DomainEntity. Each inner ColumnBinding carries its
    // own ColumnName; the inner read uses GetOrdinal(name) so SELECT column order
    // doesn't matter.
    private static void EmitNestedCompositeConstructionByOrdinalName(
        StringBuilder sb,
        ColumnBinding composite,
        string indent,
        string trailing)
    {
        if (composite.FactoryMethodName is { } factoryName)
        {
            sb.AppendLine($"{indent}// FactoryDispatch: {composite.TypeName}.{factoryName}");
            sb.AppendLine($"{indent}{composite.TypeName}.{factoryName}(");
        }
        else
        {
            sb.AppendLine($"{indent}new {composite.TypeName}(");
        }
        var inner = composite.InnerColumns;
        for (var j = 0; j < inner.Length; j++)
        {
            var b = inner[j];
            var innerTrailing = j == inner.Length - 1 ? ")" + trailing : ",";
            var readExpr = BuildOrdinalNameReadExpression(b);
            sb.AppendLine($"{indent}    {readExpr}{innerTrailing}");
        }
    }

    // v0.3-CLN1 — same as EmitNestedCompositeConstructionByOrdinalName but each inner
    // ColumnBinding reuses a pre-hoisted ordinal local (one `var __<name>_ord = ...`
    // emitted by the outer EmitOrdinalHoistsForColumns pass) instead of calling
    // GetOrdinal(<name>) inline. Required for the nullable-leaf path (IsDBNull + Get
    // would otherwise call GetOrdinal twice) and harmless for non-nullable leaves.
    private static void EmitNestedCompositeConstructionByOrdinalNameWithHoisted(
        StringBuilder sb,
        ColumnBinding composite,
        string[] innerOrdinalLocals,
        string indent,
        string trailing)
    {
        if (composite.FactoryMethodName is { } factoryName)
        {
            sb.AppendLine($"{indent}// FactoryDispatch: {composite.TypeName}.{factoryName}");
            sb.AppendLine($"{indent}{composite.TypeName}.{factoryName}(");
        }
        else
        {
            sb.AppendLine($"{indent}new {composite.TypeName}(");
        }
        var inner = composite.InnerColumns;
        for (var j = 0; j < inner.Length; j++)
        {
            var b = inner[j];
            var innerTrailing = j == inner.Length - 1 ? ")" + trailing : ",";
            var readExpr = BuildReadExpressionWithOrdinalLocal(b, innerOrdinalLocals[j]);
            sb.AppendLine($"{indent}    {readExpr}{innerTrailing}");
        }
    }

    // v0.3-CLN1 — does any column in the list carry a ColumnName? Used by the
    // multi-result emit paths (Row / List) to decide whether to emit the hoist
    // block: positional shapes (FlatRow) skip hoisting since integer literals
    // are already free.
    private static bool HasAnyColumnName(EquatableArray<ColumnBinding> columns)
    {
        foreach (var col in columns)
        {
            if (col.ColumnName is not null) return true;
        }
        return false;
    }

    // v0.3-CLN1 — emit one `var __<col>_ord = __reader.GetOrdinal("Col");` per
    // column-name-resolved column (flattening composite inner columns into their
    // own per-inner-column local). Returns a parallel jagged-array shape:
    //   result[i] = [singleOrdinalName] for a leaf column
    //   result[i] = [innerOrd0, innerOrd1, ...] for a composite column
    // Callers can index by outer column position and reuse the locals in the
    // IsDBNull guard + GetXxx read instead of inlining a second GetOrdinal call.
    private static string[]?[] EmitOrdinalHoistsForColumns(
        StringBuilder sb,
        EquatableArray<ColumnBinding> columns,
        string indent)
    {
        var hoists = new string[]?[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            var col = columns[i];
            if (col.InnerColumns.Length > 0)
            {
                var inner = col.InnerColumns;
                var innerLocals = new string[inner.Length];
                for (var j = 0; j < inner.Length; j++)
                {
                    var b = inner[j];
                    var baseName = b.ColumnName ?? b.CtorArgName ?? "col" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "_" + j.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var local = "__" + baseName + "_ord";
                    var literal = SymbolDisplay.FormatLiteral(b.ColumnName ?? string.Empty, quote: true);
                    sb.AppendLine($"{indent}var {local} = __reader.GetOrdinal({literal});");
                    innerLocals[j] = local;
                }
                hoists[i] = innerLocals;
            }
            else
            {
                var baseName = col.ColumnName ?? col.CtorArgName ?? "col" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var local = "__" + baseName + "_ord";
                var literal = SymbolDisplay.FormatLiteral(col.ColumnName ?? string.Empty, quote: true);
                sb.AppendLine($"{indent}var {local} = __reader.GetOrdinal({literal});");
                hoists[i] = new[] { local };
            }
        }
        return hoists;
    }

    // v0.3 Phase C.2 — IAsyncEnumerable<T> streaming. Emits a yield-based async
    // iterator that opens the connection (if closed), executes the reader, yields
    // one row per ReadAsync iteration, and closes the connection in finally.
    //
    // Differences from EmitFlatRow / EmitDomainEntity:
    //   * Signature carries `[EnumeratorCancellation]` on the CancellationToken
    //     parameter — required by the C# iterator state machine to thread the
    //     consumer's WithCancellation() token into the user's body.
    //   * Reader loop is `while (ReadAsync) yield return ...` rather than a
    //     single-row read.
    //   * try/finally still owns the close-on-finally lifecycle; `yield return`
    //     suspending the iterator does NOT break the open/close pairing because
    //     the C# state machine wraps the finally as a `DisposeAsync` hook on the
    //     IAsyncEnumerator, which fires whether the consumer awaits to completion
    //     or breaks out early (or cancels via the token).
    //
    // Element-binding routes through the same FlatRow positional / DomainEntity
    // column-name code as the single-row emits — m.Materialization.Kind selects
    // which read-expression shape applies.
    // v1.2 — Task<IReadOnlyList<TRow>> buffered list emit. Mirrors EmitStreaming's
    // open/read-loop/close lifecycle exactly; the per-row materialization is
    // identical (positional FlatRow or column-name-keyed DomainEntity); the
    // surrounding shape buffers into a `List<TRow>` and returns it cast to
    // `IReadOnlyList<TRow>` at the end. Issue #102.
    //
    // No [EnumeratorCancellation] requirement here (that's a Streaming-only
    // C# requirement on the user's declaration). The CancellationToken propagates
    // through the normal parameter-binding mechanism.
    private static void EmitListResultSet(StringBuilder sb, QueryMethodModel m, string connectionAccess)
    {
        var mat = m.Materialization;
        if (mat is null)
        {
            // Defensive — classification should never assign ListResultSet without
            // a model. Emit a throwing body so the missing wiring is visible at
            // first invocation rather than at compile.
            var paramListFallback = BuildParameterList(m.MethodParameters);
            sb.AppendLine($"    {GeneratedCodeAttribute}");
            sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({paramListFallback})");
            sb.AppendLine($"        => throw new global::System.InvalidOperationException(\"ZeroAlloc.ORM generator invariant: ListResultSet missing Materialization for '{m.MethodName}'.\");");
            return;
        }

        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);
        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        BuildConnectionPrologue(sb, connectionAccess, ct, "        ");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        BuildCommandTextAssignment(sb, m, "__cmd", "            ");
        EmitParameterBindingWithIndent(sb, m, "            ");
        sb.AppendLine($"            await using var __reader = await __cmd.ExecuteReaderAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine($"            var __list = new global::System.Collections.Generic.List<{mat.TargetTypeFullName}>();");
        sb.AppendLine($"            while (await __reader.ReadAsync({ct}).ConfigureAwait(false))");
        sb.AppendLine("            {");
        var cols = mat.Columns;
        var useColumnNames = mat.Kind == MaterializationKind.DomainEntity;
        // Same ordinal-hoisting pattern as Streaming/FlatRow paths: column-name
        // resolution caches the GetOrdinal call into a per-row local so the
        // IsDBNull guard and the GetXxx read share one lookup. Positional
        // FlatRow paths use integer literals directly (no hoist needed).
        string[]?[]? hoistedOrdinals = null;
        if (useColumnNames)
        {
            hoistedOrdinals = EmitOrdinalHoistsForColumns(sb, cols, "                ");
        }
        sb.AppendLine($"                __list.Add(new {mat.TargetTypeFullName}(");
        for (var i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            var trailing = i == cols.Length - 1 ? "));" : ",";
            string ordinalExpr;
            if (useColumnNames)
            {
                ordinalExpr = hoistedOrdinals![i]![0];
            }
            else
            {
                ordinalExpr = $"{i}";
            }
            var readExpr = $"__reader.{col.GetterMethod}({ordinalExpr})";
            if (col.Convention is { } conv && conv.FactoryFullName is not null)
            {
                readExpr = conv.Kind switch
                {
                    (int)ConventionKind.Enum
                        => $"({conv.FactoryFullName}){readExpr}",
                    (int)ConventionKind.EnumAsString
                        => $"global::System.Enum.Parse<{conv.FactoryFullName}>({readExpr})",
                    _ => conv.FactoryIsCtor
                        ? $"new {conv.FactoryFullName}({readExpr})"
                        : $"{conv.FactoryFullName}({readExpr})",
                };
            }
            string expr;
            if (col.IsNullable)
            {
                expr = $"__reader.IsDBNull({ordinalExpr}) ? ({col.TypeName}?)null : {readExpr}";
            }
            else
            {
                expr = readExpr;
            }
            sb.AppendLine($"                    {expr}{trailing}");
        }
        sb.AppendLine("            }");
        sb.AppendLine("            return __list;");
        BuildConnectionEpilogue(sb, "        ");
        sb.AppendLine("    }");
    }

    private static void EmitStreaming(StringBuilder sb, QueryMethodModel m, string connectionAccess)
    {
        var mat = m.Materialization;
        if (mat is null)
        {
            // Defensive — classification should never assign Streaming without a model.
            // Throw loudly so a regression here surfaces on first MoveNextAsync instead
            // of silently yielding an empty sequence. `throw` is a valid iterator method
            // body in C#; the compiler builds a state machine whose first MoveNextAsync
            // raises the exception.
            sb.AppendLine($"            throw new global::System.InvalidOperationException(\"ZeroAlloc.ORM generator invariant violation: EmitShape.Streaming reached emit with null Materialization for '{m.MethodName}'.\");");
            return;
        }

        // Streaming uses the same parameter-list shape as the single-row paths:
        // the [EnumeratorCancellation] attribute lives on the user's source
        // declaration (ZAO007-enforced) and partial-method attribute merging
        // forbids re-emitting it on the generated half (CS0579).
        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);
        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        BuildConnectionPrologue(sb, connectionAccess, ct, "        ");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        BuildCommandTextAssignment(sb, m, "__cmd", "            ");
        EmitParameterBindingWithIndent(sb, m, "            ");
        sb.AppendLine($"            await using var __reader = await __cmd.ExecuteReaderAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine($"            while (await __reader.ReadAsync({ct}).ConfigureAwait(false))");
        sb.AppendLine("            {");
        var cols = mat.Columns;
        var useColumnNames = mat.Kind == MaterializationKind.DomainEntity;
        // v0.3-CLN1 — for column-name-resolved streaming (DomainEntity element),
        // hoist GetOrdinal(<name>) into a per-row local so the IsDBNull guard and
        // GetXxx read share the same lookup. Positional reads skip hoisting; the
        // integer literal is already free.
        string[]?[]? hoistedOrdinals = null;
        if (useColumnNames)
        {
            hoistedOrdinals = EmitOrdinalHoistsForColumns(sb, cols, "                ");
        }
        sb.AppendLine($"                yield return new {mat.TargetTypeFullName}(");
        for (var i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            var trailing = i == cols.Length - 1 ? ");" : ",";
            // Positional (FlatRow record) vs column-name (DomainEntity class) routing
            // mirrors the single-row emit paths. Convention wrappers (ValueObject /
            // SingleArgCtor / StaticFactory / Enum / EnumAsString) are layered on top
            // of the raw GetXxx read identically to EmitFlatRow / EmitDomainEntity.
            string ordinalExpr;
            if (useColumnNames)
            {
                ordinalExpr = hoistedOrdinals![i]![0];
            }
            else
            {
                // Match EmitFlatRow's direct-interpolation style for positional reads.
                // Integer literals interpolate culture-invariantly in C#, so no explicit
                // InvariantCulture call is needed.
                ordinalExpr = $"{i}";
            }
            var readExpr = $"__reader.{col.GetterMethod}({ordinalExpr})";
            if (col.Convention is { } conv && conv.FactoryFullName is not null)
            {
                readExpr = conv.Kind switch
                {
                    (int)ConventionKind.Enum
                        => $"({conv.FactoryFullName}){readExpr}",
                    (int)ConventionKind.EnumAsString
                        => $"global::System.Enum.Parse<{conv.FactoryFullName}>({readExpr})",
                    _ => conv.FactoryIsCtor
                        ? $"new {conv.FactoryFullName}({readExpr})"
                        : $"{conv.FactoryFullName}({readExpr})",
                };
            }
            string expr;
            if (col.IsNullable)
            {
                expr = $"__reader.IsDBNull({ordinalExpr}) ? ({col.TypeName}?)null : {readExpr}";
            }
            else
            {
                expr = readExpr;
            }
            sb.AppendLine($"                    {expr}{trailing}");
        }
        sb.AppendLine("            }");
        BuildConnectionEpilogue(sb, "        ");
        sb.AppendLine("    }");
    }

    // v0.3 Phase B.2 — IAsyncDbBatch path for MultiResultSet. Emits one BatchCommand
    // per SQL statement (each with its own per-parameter binding block — locals are
    // suffixed `_N` to avoid collision when the same `@param` appears in multiple
    // statements), then walks result sets via NextResultAsync between elements.
    //
    // Element-kind dispatch:
    //   * Row     -> ReadAsync once, ctor invocation. First element returning empty
    //                is the canonical "no aggregate" signal -> return null when
    //                ReturnsNullable; non-first elements throw a materialization
    //                exception (the SQL said it'd produce that many result sets).
    //   * List    -> while(ReadAsync) Add(...). Empty result is legal (empty list).
    //   * Scalar  -> ReadAsync once, IsDBNull-guarded GetXxx(0). Same first-element
    //                empty contract as Row.
    private static void EmitMultiResultSetBatch(StringBuilder sb, QueryMethodModel m, string connectionAccess)
    {
        var mat = m.MultiResultMaterialization;
        if (mat is null)
        {
            // Defensive — classification should never assign MultiResultSet without a model.
            EmitMultiResultSetStub(sb, m);
            return;
        }

        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);
        var statements = SqlStatementSplitter.Split(m.Sql);

        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        BuildConnectionPrologue(sb, connectionAccess, ct, "        ");
        EmitBatchSetup(sb, m, statements);
        sb.AppendLine($"            await using var __reader = await __batch.ExecuteReaderAsync({ct}).ConfigureAwait(false);");
        EmitMultiResultElements(sb, mat, ct, indent: "            ");
        BuildConnectionEpilogue(sb, "        ");
        sb.AppendLine("    }");
    }

    // v0.3 Phase B.4 — runtime branch on __conn.CanCreateBatch. Single method body
    // that picks between the batch path (when the provider supports IAsyncDbBatch)
    // and the ;-joined fallback (when it doesn't). Both branches return the same
    // tuple shape so the partial method signature stays well-typed.
    //
    // The opening (connection, openedHere, try/finally) is shared at the method
    // level; only the inner execution + materialization differs between the two
    // branches.
    private static void EmitMultiResultSetBatchWithFallback(StringBuilder sb, QueryMethodModel m, string connectionAccess)
    {
        var mat = m.MultiResultMaterialization;
        if (mat is null)
        {
            EmitMultiResultSetStub(sb, m);
            return;
        }

        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);

        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        BuildConnectionPrologue(sb, connectionAccess, ct, "        ");
        sb.AppendLine("            if (__conn.CanCreateBatch)");
        sb.AppendLine("            {");
        EmitMultiResultSetBatchBody(sb, m, mat, ct, indent: "                ");
        sb.AppendLine("            }");
        sb.AppendLine("            else");
        sb.AppendLine("            {");
        EmitMultiResultSetJoinedBody(sb, m, mat, ct, indent: "                ");
        sb.AppendLine("            }");
        BuildConnectionEpilogue(sb, "        ");
        sb.AppendLine("    }");
    }

    // Batch-body emit (no method header or try/finally) so EmitMultiResultSetBatch
    // (BatchAlways, owning try/finally) and EmitMultiResultSetBatchWithFallback
    // (Auto, sharing try/finally) can re-use the same body shape.
    private static void EmitMultiResultSetBatchBody(StringBuilder sb, QueryMethodModel m, MultiResultMaterializationModel mat, string ct, string indent)
    {
        var statements = SqlStatementSplitter.Split(m.Sql);
        EmitBatchSetupWithIndent(sb, m, statements, indent);
        sb.AppendLine($"{indent}await using var __reader = await __batch.ExecuteReaderAsync({ct}).ConfigureAwait(false);");
        EmitMultiResultElements(sb, mat, ct, indent: indent);
    }

    // Joined-body emit (no method header or try/finally), symmetric with
    // EmitMultiResultSetBatchBody so BatchWithFallback can drop the joined path
    // into its falsy branch verbatim.
    private static void EmitMultiResultSetJoinedBody(StringBuilder sb, QueryMethodModel m, MultiResultMaterializationModel mat, string ct, string indent)
    {
        sb.AppendLine($"{indent}await using var __cmd = __conn.CreateCommand();");
        BuildCommandTextAssignment(sb, m, "__cmd", indent);
        EmitParameterBindingWithIndent(sb, m, indent);
        sb.AppendLine($"{indent}await using var __reader = await __cmd.ExecuteReaderAsync({ct}).ConfigureAwait(false);");
        EmitMultiResultElements(sb, mat, ct, indent: indent);
    }

    // Indented variant of EmitBatchSetup for the BatchWithFallback nesting. The
    // top-level BatchAlways path still calls the no-indent version through its
    // own try/finally body.
    private static void EmitBatchSetupWithIndent(StringBuilder sb, QueryMethodModel m, ImmutableArray<string> statements, string indent)
    {
        sb.AppendLine($"{indent}await using var __batch = __conn.CreateBatch();");
        for (var i = 0; i < statements.Length; i++)
        {
            var stmtLiteral = SymbolDisplay.FormatLiteral(statements[i].Trim(), quote: true);
            var cmdLocal = "__cmd" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sb.AppendLine();
            sb.AppendLine($"{indent}var {cmdLocal} = __batch.CreateBatchCommand();");
            sb.AppendLine($"{indent}{cmdLocal}.CommandText = {stmtLiteral};");
            EmitBatchCommandParameterBindingWithIndent(sb, m, cmdLocal, i, indent);
            sb.AppendLine($"{indent}__batch.BatchCommands.Add({cmdLocal});");
        }
        sb.AppendLine();
    }

    // Indented variant of EmitBatchCommandParameterBinding for the nested branch.
    private static void EmitBatchCommandParameterBindingWithIndent(StringBuilder sb, QueryMethodModel m, string cmdLocal, int cmdIndex, string indent)
    {
        foreach (var p in m.MethodParameters)
        {
            if (p.IsCancellationToken) continue;
            var local = "__p_" + p.Name + "_" + cmdIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var paramName = p.ParamNameOverride ?? ("@" + p.Name);
            var paramNameLiteral = SymbolDisplay.FormatLiteral(paramName, quote: true);
            sb.AppendLine($"{indent}var {local} = {cmdLocal}.CreateParameter();");
            sb.AppendLine($"{indent}{local}.ParameterName = {paramNameLiteral};");

            var valueExpr = "@" + p.Name;
            if (p.Convention is { } conv)
            {
                if (conv.Kind == (int)ConventionKind.Enum)
                {
                    var castType = PrimitiveCatalog.GetScalarCastTypeFromReader(conv.UnderlyingReader);
                    valueExpr = $"({castType})@{p.Name}";
                }
                else if (conv.Kind == (int)ConventionKind.EnumAsString)
                {
                    valueExpr = $"@{p.Name}.ToString()";
                }
                else if (conv.ValuePropertyName is { } propName)
                {
                    valueExpr = $"@{p.Name}.{propName}";
                }
            }

            if (p.IsNullable)
            {
                sb.AppendLine($"{indent}{local}.Value = (object?){valueExpr} ?? global::System.DBNull.Value;");
            }
            else
            {
                sb.AppendLine($"{indent}{local}.Value = {valueExpr};");
            }
            sb.AppendLine($"{indent}{cmdLocal}.Parameters.Add({local});");
        }
    }

    // Single-command parameter binding (indented variant) for the joined branch of
    // BatchWithFallback. The default-indent EmitParameterBinding stays unchanged
    // for the single-command emit paths (ScalarInt / NullableScalar / FlatRow /
    // DomainEntity / JoinedStatementsOnly) so v0.1/v0.2 snapshots remain stable.
    //
    // v0.4 Phase E (post-review Fix 5): the SprocWithOutputParams shape needs the
    // same INPUT-binding emit but flips Direction = Output for the subset of
    // parameters whose names match output tuple positions. Rather than maintain a
    // forked clone (the former EmitSprocOutputParamsBinding) we accept an optional
    // `outputParamNames` set here; when non-null and the current parameter's name
    // is present, the emit skips the Value assignment and writes the Direction
    // line. All other callers pass `null` so the v0.1/v0.2/v0.3 snapshot byte-output
    // is unchanged.
    private static void EmitParameterBindingWithIndent(
        StringBuilder sb,
        QueryMethodModel m,
        string indent,
        HashSet<string>? outputParamNames = null)
    {
        foreach (var p in m.MethodParameters)
        {
            if (p.IsCancellationToken) continue;

            // v0.5 Phase B — composite parameter unpacks into N DbParameter
            // blocks, one per inner ctor argument. The sentinel comment pins
            // the classifier branch (B.1) and the actual unpack emit follows.
            // EquatableArray<T>.Length returns 0 for the default state, so the
            // bare `Length > 0` check is sufficient to distinguish a populated
            // composite from a primitive/VO binding.
            if (p.CompositeFields.Length > 0)
            {
                sb.AppendLine($"{indent}// EmitShape: CompositeBinding {p.Name} -> {p.CompositeTypeFullName} (fields: {p.CompositeFields.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
                EmitCompositeParameterBinding(sb, p, "__cmd", indent);
                continue;
            }

            var local = "__p_" + p.Name;
            var paramName = p.ParamNameOverride ?? ("@" + p.Name);
            var paramNameLiteral = SymbolDisplay.FormatLiteral(paramName, quote: true);
            sb.AppendLine($"{indent}var {local} = __cmd.CreateParameter();");
            sb.AppendLine($"{indent}{local}.ParameterName = {paramNameLiteral};");

            var isOutput = outputParamNames is not null && outputParamNames.Contains(p.Name);
            if (isOutput)
            {
                // Output position: set Direction and skip the .Value write — the
                // procedure populates the value after execution; assigning a CLR
                // value upfront is either ignored or treated as the initial state
                // by the provider, so leaving it unset is cleaner.
                sb.AppendLine($"{indent}{local}.Direction = global::System.Data.ParameterDirection.Output;");
            }
            else
            {
                var valueExpr = "@" + p.Name;
                if (p.Convention is { } conv)
                {
                    if (conv.Kind == (int)ConventionKind.Enum)
                    {
                        var castType = PrimitiveCatalog.GetScalarCastTypeFromReader(conv.UnderlyingReader);
                        valueExpr = $"({castType})@{p.Name}";
                    }
                    else if (conv.Kind == (int)ConventionKind.EnumAsString)
                    {
                        valueExpr = $"@{p.Name}.ToString()";
                    }
                    else if (conv.ValuePropertyName is { } propName)
                    {
                        valueExpr = $"@{p.Name}.{propName}";
                    }
                }

                if (p.IsNullable)
                {
                    sb.AppendLine($"{indent}{local}.Value = (object?){valueExpr} ?? global::System.DBNull.Value;");
                }
                else
                {
                    sb.AppendLine($"{indent}{local}.Value = {valueExpr};");
                }
            }
            sb.AppendLine($"{indent}__cmd.Parameters.Add({local});");
        }
    }

    // v0.3 Phase B.3 — ;-joined fallback for MultiResultSet. Used for
    // BatchEmitStrategy.JoinedStatementsOnly when the adopter has explicitly set
    // BatchMode.Never. Also called by the falsy branch of BatchWithFallback (B.4)
    // for providers without IAsyncDbBatch support.
    //
    // Differences from EmitMultiResultSetBatch:
    //   * Single command (the SQL already contains the `;` separators) — no batch.
    //   * Parameters bound ONCE — the joined SQL re-uses the same `@param` across
    //     statements; the provider re-binds for each as a single command execution.
    //   * Per-element reader walk is identical (shared via EmitMultiResultElements).
    private static void EmitMultiResultSetJoined(StringBuilder sb, QueryMethodModel m, string connectionAccess)
    {
        var mat = m.MultiResultMaterialization;
        if (mat is null)
        {
            EmitMultiResultSetStub(sb, m);
            return;
        }

        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);
        // The original SQL already carries `;` separators between statements — emit
        // it verbatim via BuildCommandTextAssignment. Joining via
        // `string.Join("; ", Split(...))` would normalize whitespace, which is
        // desirable but loses the user's original layout in multi-line raw string
        // SQL. Verbatim keeps snapshots predictable.

        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        BuildConnectionPrologue(sb, connectionAccess, ct, "        ");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        BuildCommandTextAssignment(sb, m, "__cmd", "            ");
        EmitParameterBinding(sb, m);
        sb.AppendLine($"            await using var __reader = await __cmd.ExecuteReaderAsync({ct}).ConfigureAwait(false);");
        EmitMultiResultElements(sb, mat, ct, indent: "            ");
        BuildConnectionEpilogue(sb, "        ");
        sb.AppendLine("    }");
    }

    // Render the `__batch` + per-statement BatchCommand setup. Each statement gets its
    // own block scope so the per-command `__p_<name>_N` locals don't leak. Parameter
    // binding mirrors EmitParameterBinding but each parameter local is suffixed with
    // the command index to keep them distinct.
    private static void EmitBatchSetup(StringBuilder sb, QueryMethodModel m, ImmutableArray<string> statements)
    {
        sb.AppendLine("            await using var __batch = __conn.CreateBatch();");
        for (var i = 0; i < statements.Length; i++)
        {
            var stmtLiteral = SymbolDisplay.FormatLiteral(statements[i].Trim(), quote: true);
            var cmdLocal = "__cmd" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sb.AppendLine();
            sb.AppendLine($"            var {cmdLocal} = __batch.CreateBatchCommand();");
            sb.AppendLine($"            {cmdLocal}.CommandText = {stmtLiteral};");
            EmitBatchCommandParameterBinding(sb, m, cmdLocal, i);
            sb.AppendLine($"            __batch.BatchCommands.Add({cmdLocal});");
        }
        sb.AppendLine();
    }

    // Per-statement parameter binding. Identical to EmitParameterBinding apart from
    // (a) targeting a specific `__cmdN` local instead of the implicit `__cmd` and
    // (b) suffixing the per-parameter local with the command index so two commands
    // referencing the same `@id` don't collide on `__p_id`.
    private static void EmitBatchCommandParameterBinding(StringBuilder sb, QueryMethodModel m, string cmdLocal, int cmdIndex)
    {
        foreach (var p in m.MethodParameters)
        {
            if (p.IsCancellationToken) continue;

            // v0.5 Phase B — composite parameter unpacks per-batch-statement.
            // The cmdIndex suffix keeps locals distinct across batch statements
            // so `@total_Amount` in two statements gets two unique
            // `__p_total_Amount_0` / `__p_total_Amount_1` locals.
            if (p.CompositeFields.Length > 0)
            {
                sb.AppendLine($"            // EmitShape: CompositeBinding {p.Name} -> {p.CompositeTypeFullName} (fields: {p.CompositeFields.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
                EmitCompositeParameterBinding(sb, p, cmdLocal, "            ", cmdIndex);
                continue;
            }

            var local = "__p_" + p.Name + "_" + cmdIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var paramName = p.ParamNameOverride ?? ("@" + p.Name);
            var paramNameLiteral = SymbolDisplay.FormatLiteral(paramName, quote: true);
            sb.AppendLine($"            var {local} = {cmdLocal}.CreateParameter();");
            sb.AppendLine($"            {local}.ParameterName = {paramNameLiteral};");

            var valueExpr = "@" + p.Name;
            if (p.Convention is { } conv)
            {
                if (conv.Kind == (int)ConventionKind.Enum)
                {
                    var castType = PrimitiveCatalog.GetScalarCastTypeFromReader(conv.UnderlyingReader);
                    valueExpr = $"({castType})@{p.Name}";
                }
                else if (conv.Kind == (int)ConventionKind.EnumAsString)
                {
                    valueExpr = $"@{p.Name}.ToString()";
                }
                else if (conv.ValuePropertyName is { } propName)
                {
                    valueExpr = $"@{p.Name}.{propName}";
                }
            }

            if (p.IsNullable)
            {
                sb.AppendLine($"            {local}.Value = (object?){valueExpr} ?? global::System.DBNull.Value;");
            }
            else
            {
                sb.AppendLine($"            {local}.Value = {valueExpr};");
            }
            sb.AppendLine($"            {cmdLocal}.Parameters.Add({local});");
        }
    }

    // Shared per-tuple-element materialization. Used by both the batch (B.2) and
    // joined-fallback (B.3) emit paths — same reader semantics either way once the
    // reader is open.
    //
    // The first non-List element gates the optional "return null" on empty (this is
    // the canonical "no aggregate present" signal). For non-first elements that
    // expected a fresh result set, NextResultAsync returning false throws the
    // ZeroAllocOrmMaterializationException so the adopter sees a clear runtime
    // signal that the database returned fewer result sets than the tuple declared.
    //
    // v0.4 Phase E (post-review Fix 4): the SprocWithOutputParams shape reuses this
    // loop with `declareLocal: false` and `emitReturnTuple: false` so the locals
    // assigned inside the await-using block are PRE-DECLARED outside it (the final
    // tuple construction happens after the reader disposes + the parameter
    // readback finishes). `returnsNullable` is hard-coded to `false` for the sproc
    // caller (see TryBuildSprocOutputParamsMaterialization for the rejection of
    // the nullable-outer-tuple case).
    private static void EmitMultiResultElements(
        StringBuilder sb,
        MultiResultMaterializationModel mat,
        string ct,
        string indent,
        bool declareLocal = true,
        bool emitReturnTuple = true)
    {
        EmitMultiResultElements(sb, mat.Elements, mat.ReturnsNullable, ct, indent, declareLocal, emitReturnTuple);
    }

    // Element-array overload — the sproc path supplies its own EquatableArray
    // (interleaved with the output-param positions) and needs the SAME emit shape.
    private static void EmitMultiResultElements(
        StringBuilder sb,
        EquatableArray<MultiResultElement> elements,
        bool returnsNullable,
        string ct,
        string indent,
        bool declareLocal = true,
        bool emitReturnTuple = true)
    {
        for (var i = 0; i < elements.Length; i++)
        {
            var el = elements[i];
            if (i > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"{indent}if (!await __reader.NextResultAsync({ct}).ConfigureAwait(false))");
                sb.AppendLine($"{indent}    throw new global::ZeroAlloc.ORM.ZeroAllocOrmMaterializationException(\"Expected {elements.Length} result sets; got \" + {i.ToString(System.Globalization.CultureInfo.InvariantCulture)} + \".\");");
            }
            EmitMultiResultElement(sb, el, i, returnsNullable, ct, indent, declareLocal);
        }

        if (!emitReturnTuple) return;

        // Build the return tuple expression from the per-element locals.
        sb.Append($"{indent}return (");
        for (var i = 0; i < elements.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("__elem").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        sb.AppendLine(");");
    }

    // `declareLocal: true` -> `var __elem<i> = ...;` (multi-result emit).
    // `declareLocal: false` -> `__elem<i> = ...;` (sproc emit; the locals are
    // pre-declared outside the await-using block via EmitSprocResultElementDeclarations
    // so the final tuple can reference them after the reader disposes).
    private static void EmitMultiResultElement(
        StringBuilder sb,
        MultiResultElement el,
        int index,
        bool returnsNullable,
        string ct,
        string indent,
        bool declareLocal = true)
    {
        var localName = "__elem" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var declarePrefix = declareLocal ? "var " : string.Empty;
        switch (el.Kind)
        {
            case MultiResultElementKind.Row:
                {
                    sb.AppendLine($"{indent}if (!await __reader.ReadAsync({ct}).ConfigureAwait(false))");
                    if (returnsNullable && index == 0)
                    {
                        // First-row-of-first-set empty => return null. Only fires for
                        // Task<(...)?> — non-nullable returns fall through to the
                        // exception arm below.
                        sb.AppendLine($"{indent}    return null;");
                    }
                    else
                    {
                        sb.AppendLine($"{indent}    throw new global::ZeroAlloc.ORM.ZeroAllocOrmMaterializationException(\"Expected at least one row in result set " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".\");");
                    }
                    // v0.3-CLN1 — hoist GetOrdinal(<name>) per column-name-resolved column.
                    var rowHoists = HasAnyColumnName(el.Columns)
                        ? EmitOrdinalHoistsForColumns(sb, el.Columns, indent)
                        : null;
                    sb.AppendLine($"{indent}{declarePrefix}{localName} = new {el.ElementTypeName}(");
                    EmitColumnReads(sb, el.Columns, indent + "    ", trailing: ");", rowHoists);
                    break;
                }
            case MultiResultElementKind.List:
                {
                    sb.AppendLine($"{indent}{declarePrefix}{localName} = new global::System.Collections.Generic.List<{el.ElementTypeName}>();");
                    sb.AppendLine($"{indent}while (await __reader.ReadAsync({ct}).ConfigureAwait(false))");
                    sb.AppendLine($"{indent}{{");
                    // v0.3-CLN1 — hoist GetOrdinal(<name>) per row inside the loop.
                    var listHoists = HasAnyColumnName(el.Columns)
                        ? EmitOrdinalHoistsForColumns(sb, el.Columns, indent + "    ")
                        : null;
                    sb.AppendLine($"{indent}    {localName}.Add(new {el.ElementTypeName}(");
                    EmitColumnReads(sb, el.Columns, indent + "        ", trailing: "));", listHoists);
                    sb.AppendLine($"{indent}}}");
                    break;
                }
            case MultiResultElementKind.Scalar:
                {
                    sb.AppendLine($"{indent}if (!await __reader.ReadAsync({ct}).ConfigureAwait(false))");
                    if (returnsNullable && index == 0)
                    {
                        sb.AppendLine($"{indent}    return null;");
                    }
                    else
                    {
                        sb.AppendLine($"{indent}    throw new global::ZeroAlloc.ORM.ZeroAllocOrmMaterializationException(\"Expected at least one row in result set " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".\");");
                    }
                    var readExpr = $"__reader.{el.GetterMethod}(0)";
                    if (el.Convention is { } conv && conv.FactoryFullName is not null)
                    {
                        readExpr = conv.Kind switch
                        {
                            (int)ConventionKind.Enum
                                => $"({conv.FactoryFullName}){readExpr}",
                            (int)ConventionKind.EnumAsString
                                => $"global::System.Enum.Parse<{conv.FactoryFullName}>({readExpr})",
                            _ => conv.FactoryIsCtor
                                ? $"new {conv.FactoryFullName}({readExpr})"
                                : $"{conv.FactoryFullName}({readExpr})",
                        };
                    }
                    if (el.IsNullable)
                    {
                        sb.AppendLine($"{indent}{declarePrefix}{localName} = __reader.IsDBNull(0) ? ({el.ElementTypeName}?)null : {readExpr};");
                    }
                    else
                    {
                        sb.AppendLine($"{indent}{declarePrefix}{localName} = {readExpr};");
                    }
                    break;
                }
        }
    }

    // Render the ordered column-read expressions for a Row / List element. Mirrors
    // the EmitFlatRow column loop but emits one expression per line with a trailing
    // comma except for the last. The `trailing` parameter lets the caller close the
    // outer ctor call appropriately (")" for Row, "))" for List's inner Add).
    //
    // v0.3-CLN1 — when `hoistedOrdinals` is non-null, column-name-resolved columns
    // reuse pre-emitted ordinal locals instead of calling GetOrdinal(<name>) inline.
    // The caller emits the hoists at the right indent (Row: outside the `new T(`
    // call; List: inside the while-loop body before the `Add(new T(` line) so the
    // local is alive for both the IsDBNull guard and the GetXxx read.
    private static void EmitColumnReads(StringBuilder sb, EquatableArray<ColumnBinding> cols, string indent, string trailing, string[]?[]? hoistedOrdinals = null)
    {
        for (var i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            var isLast = i == cols.Length - 1;
            string ordinalExpr;
            // Mirror EmitFlatRow / EmitDomainEntity: positional FlatRow uses ordinal
            // index; column-name DomainEntity routes through GetOrdinal("Name").
            if (col.ColumnName is { } columnName)
            {
                if (hoistedOrdinals is not null)
                {
                    ordinalExpr = hoistedOrdinals[i]![0];
                }
                else
                {
                    var colNameLiteral = SymbolDisplay.FormatLiteral(columnName, quote: true);
                    ordinalExpr = $"__reader.GetOrdinal({colNameLiteral})";
                }
            }
            else
            {
                ordinalExpr = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            var readExpr = $"__reader.{col.GetterMethod}({ordinalExpr})";
            if (col.Convention is { } conv && conv.FactoryFullName is not null)
            {
                readExpr = conv.Kind switch
                {
                    (int)ConventionKind.Enum
                        => $"({conv.FactoryFullName}){readExpr}",
                    (int)ConventionKind.EnumAsString
                        => $"global::System.Enum.Parse<{conv.FactoryFullName}>({readExpr})",
                    _ => conv.FactoryIsCtor
                        ? $"new {conv.FactoryFullName}({readExpr})"
                        : $"{conv.FactoryFullName}({readExpr})",
                };
            }
            string expr;
            if (col.IsNullable)
            {
                expr = $"__reader.IsDBNull({ordinalExpr}) ? ({col.TypeName}?)null : {readExpr}";
            }
            else
            {
                expr = readExpr;
            }
            var lineTrailing = isLast ? trailing : ",";
            sb.AppendLine($"{indent}{expr}{lineTrailing}");
        }
    }

    // v0.3 Phase B.1 — stub emit for the MultiResultSet shape. The detection has
    // landed but the real emit lands in B.2-B.4 (batch path, joined fallback, runtime
    // branch). We still need a method body — CS8795 requires a partial method to have
    // an implementation — so we throw a clear NotImplementedException that points
    // adopters at the milestone where the path becomes live.
    private static void EmitMultiResultSetStub(StringBuilder sb, QueryMethodModel m)
    {
        var paramList = BuildParameterList(m.MethodParameters);
        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("        => throw new global::System.NotImplementedException(\"MultiResultSet emit lands in v0.3 Phase B.2-B.4\");");
    }

    // Emit DbParameter binding for each user-declared method parameter (CancellationToken
    // skipped — it's a runtime control signal, not a SQL value). v0.1 surface: bind the
    // C# value directly to `DbParameter.Value` and let the provider infer the DbType
    // from the runtime type. Explicit DbType override + null-guard land in 6.2 and 6.3.
    //
    // Emit shape per parameter:
    //     var __p_<name> = __cmd.CreateParameter();
    //     __p_<name>.ParameterName = "@<name>";
    //     __p_<name>.Value = <name>;
    //     __cmd.Parameters.Add(__p_<name>);
    private static void EmitParameterBinding(StringBuilder sb, QueryMethodModel m)
    {
        foreach (var p in m.MethodParameters)
        {
            if (p.IsCancellationToken) continue;

            // v0.5 Phase B — composite parameter unpacks into N DbParameter
            // blocks (see EmitCompositeParameterBinding). The sentinel comment
            // matches the EmitParameterBindingWithIndent path so detection
            // tests work uniformly across single-command and batch emits.
            if (p.CompositeFields.Length > 0)
            {
                sb.AppendLine($"            // EmitShape: CompositeBinding {p.Name} -> {p.CompositeTypeFullName} (fields: {p.CompositeFields.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
                EmitCompositeParameterBinding(sb, p, "__cmd", "            ");
                continue;
            }

            var local = "__p_" + p.Name;
            var paramName = p.ParamNameOverride ?? ("@" + p.Name);
            var paramNameLiteral = SymbolDisplay.FormatLiteral(paramName, quote: true);
            sb.AppendLine($"            var {local} = __cmd.CreateParameter();");
            sb.AppendLine($"            {local}.ParameterName = {paramNameLiteral};");

            // Phase C: ValueObject / SingleArgCtor / StaticFactory parameters unwrap
            // through their discovered `Value` property before binding to the
            // DbParameter so the provider sees the underlying primitive. Phase D
            // extends this to enums: default-int round-trip casts via `(int)@x`,
            // [StoreAsString] uses `@x.ToString()`. Falls back to the raw value when
            // no convention applies — the v0.1 emit path stays byte-identical.
            var valueExpr = "@" + p.Name;
            if (p.Convention is { } conv)
            {
                if (conv.Kind == (int)ConventionKind.Enum)
                {
                    // Cast to the enum's underlying primitive. C# allows `(int)Color.Red`
                    // for an int-backed enum; for byte/short/long-backed enums the cast
                    // target derives from the GetXxx reader name. The provider then sees
                    // the integral value; SQL stores it as INTEGER.
                    var castType = PrimitiveCatalog.GetScalarCastTypeFromReader(conv.UnderlyingReader);
                    valueExpr = $"({castType})@{p.Name}";
                }
                else if (conv.Kind == (int)ConventionKind.EnumAsString)
                {
                    // Round-trip as the enum member name. ToString() is allocating but
                    // [StoreAsString] is opt-in; a source-generated parse/format table
                    // is a v0.3+ optimization.
                    valueExpr = $"@{p.Name}.ToString()";
                }
                else if (conv.ValuePropertyName is { } propName)
                {
                    valueExpr = $"@{p.Name}.{propName}";
                }
            }

            // Nullable parameters need a DBNull sentinel — assigning a CLR null to
            // DbParameter.Value is provider-dependent (some treat it as "missing
            // parameter" rather than "SQL NULL"), so we route through DBNull.Value
            // explicitly. Non-nullable parameters skip the cast for cleaner emit.
            if (p.IsNullable)
            {
                sb.AppendLine($"            {local}.Value = (object?){valueExpr} ?? global::System.DBNull.Value;");
            }
            else
            {
                sb.AppendLine($"            {local}.Value = {valueExpr};");
            }
            sb.AppendLine($"            __cmd.Parameters.Add({local});");
        }
    }

    // Forward the user's CancellationToken parameter to OpenAsync / ReadAsync /
    // ExecuteScalarAsync. If the user named their CT parameter with a C# keyword
    // (e.g. `@event`), the body must reference it with an `@`-prefix or the emit
    // produces `OpenAsync(event)` and trips CS1525.
    //
    // We ONLY `@`-prefix when the name actually is a keyword — for ordinary names
    // like `ct` or `cancellationToken` the bare identifier compiles cleanly and
    // keeps existing snapshots stable.
    //
    // Fallback `"default"` (no CT param) is the literal `default` expression, not
    // an identifier, so it must NEVER be `@`-prefixed.
    private static string FormatCancellationTokenReference(string? cancellationTokenName)
    {
        if (cancellationTokenName is null) return "default";
        return SyntaxFacts.GetKeywordKind(cancellationTokenName) != SyntaxKind.None
            ? "@" + cancellationTokenName
            : cancellationTokenName;
    }

    // v0.5 Phase B — composite parameter unpacking. For a `Money(decimal Amount,
    // string Currency)` parameter named `total`, this emits two DbParameter
    // blocks:
    //
    //   var __p_total_Amount = __cmd.CreateParameter();
    //   __p_total_Amount.ParameterName = "@total_Amount";
    //   __p_total_Amount.Value = @total.Amount;
    //   __cmd.Parameters.Add(__p_total_Amount);
    //
    //   var __p_total_Currency = __cmd.CreateParameter();
    //   __p_total_Currency.ParameterName = "@total_Currency";
    //   __p_total_Currency.Value = @total.Currency;
    //   __cmd.Parameters.Add(__p_total_Currency);
    //
    // The accessor `@total.{CtorArgName}` exploits the fact that positional
    // records auto-generate properties whose names match the ctor parameter
    // names. Convention-bearing inner fields (a VO field like `OrderId
    // Currency`) unwrap through their `.Value` accessor at bind time —
    // recursive convention layering, matching the Phase A read side.
    //
    // ParamNameOverride is intentionally NOT consulted here — Phase B picks
    // a positional convention (`@{paramName}_{ctorArgName}`) and the override
    // (which targets a single DbParameter name) doesn't compose with N-way
    // unpacking. Detection-side ZAO063 reports the misuse so adopters get a
    // build-time error instead of a silently-dropped override.
    //
    // The optional cmdIndex parameter switches the helper between the
    // single-command path (null -> locals named `__p_total_Amount`) and the
    // batch path (integer -> `__p_total_Amount_{idx}` so two statements
    // referencing the same composite don't collide on `__p_total_Amount`).
    // The wire-level DbParameter name (`@total_Amount`) is the same in both
    // cases — batches re-bind each statement's parameter set independently.
    private static void EmitCompositeParameterBinding(
        StringBuilder sb,
        ParameterInfo p,
        string cmdLocal,
        string indent,
        int? cmdIndex = null)
    {
        var localSuffix = cmdIndex is { } idx
            ? "_" + idx.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "";

        // v0.5 Phase C.2 — Nullable composite parameter (`Money? total`). The
        // all-or-nothing bind contract mirrors the materialization side: when
        // the C# value is null, every inner DbParameter binds DBNull. Switch
        // on `@<name> is null`:
        //
        //   if (@total is null)
        //   {
        //       // create N DbParameters, all with Value = DBNull.Value
        //   }
        //   else
        //   {
        //       // standard positional unpacking using @total.Value.Amount etc.
        //   }
        //
        // The non-null branch unwraps via `.Value` — this branch is reached
        // ONLY for `Nullable<T>` (value-type composite). Reference-type
        // nullable composites (`OrderRow?` where OrderRow is a class) are
        // gated out at the classifier in TransformMethod (post-review Fix 1):
        // they leave `CompositeFields` empty and fall through to ZAO041 so
        // the emit never reaches this `.Value` path. Reference-type nullable
        // composites are a future enhancement (Option B in the review notes).
        if (p.IsNullable)
        {
            sb.AppendLine($"{indent}if (@{p.Name} is null)");
            sb.AppendLine($"{indent}{{");
            foreach (var field in p.CompositeFields)
            {
                var local = "__p_" + p.Name + "_" + field.CtorArgName + localSuffix;
                var paramName = "@" + p.Name + "_" + field.CtorArgName;
                var paramNameLiteral = SymbolDisplay.FormatLiteral(paramName, quote: true);
                sb.AppendLine($"{indent}    var {local} = {cmdLocal}.CreateParameter();");
                sb.AppendLine($"{indent}    {local}.ParameterName = {paramNameLiteral};");
                sb.AppendLine($"{indent}    {local}.Value = global::System.DBNull.Value;");
                sb.AppendLine($"{indent}    {cmdLocal}.Parameters.Add({local});");
            }
            sb.AppendLine($"{indent}}}");
            sb.AppendLine($"{indent}else");
            sb.AppendLine($"{indent}{{");
            foreach (var field in p.CompositeFields)
            {
                var local = "__p_" + p.Name + "_" + field.CtorArgName + localSuffix;
                var paramName = "@" + p.Name + "_" + field.CtorArgName;
                var paramNameLiteral = SymbolDisplay.FormatLiteral(paramName, quote: true);
                sb.AppendLine($"{indent}    var {local} = {cmdLocal}.CreateParameter();");
                sb.AppendLine($"{indent}    {local}.ParameterName = {paramNameLiteral};");

                // Nullable composite accessor: `@total.Value.Amount` (Nullable<T>
                // struct path) — the C# Nullable<T> properties go through `.Value`
                // before reaching the inner ctor-arg property. The standard
                // BuildCompositeFieldValueExpression produces `@total.Amount`;
                // we patch that to `@total.Value.Amount` by injecting `.Value`
                // between the parameter name and the field accessor.
                var valueExpr = BuildCompositeFieldValueExpressionForNullable(p.Name, field);
                if (field.IsNullable)
                {
                    sb.AppendLine($"{indent}    {local}.Value = (object?){valueExpr} ?? global::System.DBNull.Value;");
                }
                else
                {
                    sb.AppendLine($"{indent}    {local}.Value = {valueExpr};");
                }
                sb.AppendLine($"{indent}    {cmdLocal}.Parameters.Add({local});");
            }
            sb.AppendLine($"{indent}}}");
            return;
        }

        foreach (var field in p.CompositeFields)
        {
            var local = "__p_" + p.Name + "_" + field.CtorArgName + localSuffix;
            var paramName = "@" + p.Name + "_" + field.CtorArgName;
            var paramNameLiteral = SymbolDisplay.FormatLiteral(paramName, quote: true);
            sb.AppendLine($"{indent}var {local} = {cmdLocal}.CreateParameter();");
            sb.AppendLine($"{indent}{local}.ParameterName = {paramNameLiteral};");

            var valueExpr = BuildCompositeFieldValueExpression(p.Name, field);
            if (field.IsNullable)
            {
                sb.AppendLine($"{indent}{local}.Value = (object?){valueExpr} ?? global::System.DBNull.Value;");
            }
            else
            {
                sb.AppendLine($"{indent}{local}.Value = {valueExpr};");
            }
            sb.AppendLine($"{indent}{cmdLocal}.Parameters.Add({local});");
        }
    }

    // v0.5 Phase C.2 — variant of BuildCompositeFieldValueExpression for the
    // non-null branch of a nullable composite parameter. The outer accessor is
    // `@total.Value` (Nullable<T> struct path) — adding `.Value` between the
    // parameter name and the field name.
    //
    // Reference-type nullable composites are gated to ZAO041 fallback at the
    // classifier in TransformMethod (post-review Fix 1) — they never reach
    // this emit branch. This helper only handles `Nullable<T>` value-type
    // composites; the `.Value` accessor is the Nullable<T> unwrap. A future
    // enhancement (Option B in the review notes) would branch on class-vs-
    // struct here and omit `.Value` for class composites.
    private static string BuildCompositeFieldValueExpressionForNullable(string paramName, CompositeBindingField field)
    {
        // Defensive `@`-prefix on the inner property accessor (see
        // BuildCompositeFieldValueExpression for rationale). The outer
        // `.Value` between the parameter name and the inner field name is
        // the Nullable<T> unwrap — only value-type composites reach here.
        var baseExpr = "@" + paramName + ".Value.@" + field.CtorArgName;
        if (field.Convention is { } conv)
        {
            if (conv.Kind == (int)ConventionKind.Enum)
            {
                var castType = PrimitiveCatalog.GetScalarCastTypeFromReader(conv.UnderlyingReader);
                return $"({castType}){baseExpr}";
            }
            if (conv.Kind == (int)ConventionKind.EnumAsString)
            {
                return $"{baseExpr}.ToString()";
            }
            if (conv.ValuePropertyName is { } propName)
            {
                return $"{baseExpr}.{propName}";
            }
        }
        return baseExpr;
    }

    // Build the C# expression that reads one inner field of a composite
    // parameter at bind time. Mirrors the Phase D parameter-convention logic
    // (Value / cast / ToString) at one extra level of indirection — the outer
    // accessor is `@{paramName}.{CtorArgName}` and the inner convention
    // unwraps that further:
    //
    //   Primitive       -> @total.Amount
    //   ValueObject     -> @total.Currency.Value
    //   Enum (int)      -> (int)@total.Status
    //   EnumAsString    -> @total.Status.ToString()
    //
    // For nullable inner fields the caller wraps the result with the DBNull
    // sentinel — same shape as the v0.4 NullableParameter emit.
    private static string BuildCompositeFieldValueExpression(string paramName, CompositeBindingField field)
    {
        // Defensive `@`-prefix on the inner property accessor. Positional records
        // auto-generate properties whose names match the ctor arg name verbatim,
        // so if the ctor arg is a C# keyword (e.g. `record Foo(int @class)`) the
        // property is also `@class` and the bare `@total.class` reference is a
        // CS1525 / CS1041 parse error. Prefixing every inner accessor keeps the
        // emit safe for keyword names without changing semantics for ordinary
        // identifiers — `@total.@Amount` is the same expression as `@total.Amount`.
        var baseExpr = "@" + paramName + ".@" + field.CtorArgName;
        if (field.Convention is { } conv)
        {
            if (conv.Kind == (int)ConventionKind.Enum)
            {
                var castType = PrimitiveCatalog.GetScalarCastTypeFromReader(conv.UnderlyingReader);
                return $"({castType}){baseExpr}";
            }
            if (conv.Kind == (int)ConventionKind.EnumAsString)
            {
                return $"{baseExpr}.ToString()";
            }
            if (conv.ValuePropertyName is { } propName)
            {
                return $"{baseExpr}.{propName}";
            }
        }
        return baseExpr;
    }

    // Render the partial method's parameter list, preserving the user's declared order
    // verbatim (including where they placed the CancellationToken). The emitted partial
    // must match the user's signature exactly or partial-method binding fails with
    // CS8795 / CS0759. The CT parameter is referenced in the body via
    // m.CancellationTokenParameterName so we never assume a hardcoded name.
    private static string BuildParameterList(EquatableArray<ParameterInfo> methodParameters)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var p in methodParameters)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(p.TypeDisplay).Append(" @").Append(p.Name);
        }
        return sb.ToString();
    }
}
