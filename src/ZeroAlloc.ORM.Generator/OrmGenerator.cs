using System;
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
    private const string IAsyncDbConnectionFullName = "System.Data.Async.IAsyncDbConnection";
    private const string IAsyncDbConnectionSimpleName = "IAsyncDbConnection";
    private const string GeneratorVersion = "0.1.0";
    private const string GeneratedCodeAttribute =
        "[global::System.CodeDom.Compiler.GeneratedCode(\"ZeroAlloc.ORM.Generator\", \"" + GeneratorVersion + "\")]";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var queryMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: QueryAttributeFullName,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => TransformMethod(ctx))
            .Where(static m => m is not null)!;

        // Group by containing-type FQN. Every QueryMethodWithTypeContext within a
        // group carries the same type-scoped fields (ConnectionAccess, partial-ness,
        // location, etc.) — we take them from g.First() instead of duplicating them
        // on each QueryMethodModel. R8 hoist: this removes the prior fallback that
        // grabbed type-properties off `repo.Methods.Values[0]` downstream.
        var grouped = queryMethods.Collect()
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

    private static QueryMethodWithTypeContext? TransformMethod(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol method) return null;
        if (method.ContainingType is not INamedTypeSymbol containing) return null;
        if (ctx.TargetNode is not MethodDeclarationSyntax methodSyntax) return null;

        // ConventionDiscovery needs a Compilation for well-known attribute lookups. We
        // construct the context once per method and re-use it across return-type and
        // per-parameter classification so the lookup cost amortizes.
        var conventionContext = new ConventionContext(ctx.SemanticModel.Compilation);

        var queryAttribute = ctx.Attributes.FirstOrDefault();
        var sql = queryAttribute?
            .ConstructorArguments
            .FirstOrDefault().Value as string ?? string.Empty;

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

        // ZAO001 — method must be partial.
        if (!methodSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
        {
            diagnostics.Add(new DiagnosticInfo(
                DescriptorId: "ZAO001",
                Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(method.Name))));
        }

        // ZAO005 — multiple [Query] attributes on one method.
        // v0.1 only ships [Query]; [Command] and [StoredProcedure] return in v0.4 at
        // which point this check expands to cover all three ORM attributes.
        var ormAttrCount = method.GetAttributes()
            .Count(a => string.Equals(
                a.AttributeClass?.ToDisplayString(),
                "ZeroAlloc.ORM.QueryAttribute",
                StringComparison.Ordinal));
        if (ormAttrCount > 1)
        {
            diagnostics.Add(new DiagnosticInfo(
                DescriptorId: "ZAO005",
                Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(method.Name))));
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
        if (SqlStatementSplitter.CountStatements(sql) > 1 && !IsMultiResultReturnType(method.ReturnType))
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

        // ZAO020 / ZAO021 — informational notes for [Query] options that the
        // generator accepts at the type level but does not yet honor at emit time.
        // The values still flow through but are silently ignored by codegen; the
        // info diagnostics keep adopters from believing they're getting behavior
        // that isn't there. Both fire only when the attribute author explicitly
        // set the non-default value.
        //
        // The Batch named-argument is also read once here and fed into
        // ResolveBatchStrategy below — keeping the read in one place avoids a
        // second scan over the attribute's NamedArguments.
        var batchMode = 0; // BatchMode.Auto default
        if (queryAttribute is not null)
        {
            foreach (var named in queryAttribute.NamedArguments)
            {
                if (string.Equals(named.Key, "FromResource", StringComparison.Ordinal)
                    && named.Value.Value is bool fromResource
                    && fromResource)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DescriptorId: "ZAO020",
                        Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                        MessageArgs: new EquatableArray<string>(ImmutableArray.Create(method.Name))));
                }
                else if (string.Equals(named.Key, "Batch", StringComparison.Ordinal)
                    && named.Value.Value is int batchValue)
                {
                    batchMode = batchValue;
                    if (batchValue != 0) // BatchMode.Auto == 0
                    {
                        // Map the enum integer back to its name for the diagnostic message.
                        // BatchMode lives in ZeroAlloc.ORM; we mirror the enum order here
                        // to avoid a hard reference from the generator (netstandard2.0) to
                        // the abstractions assembly (net10.0).
                        var batchName = batchValue switch
                        {
                            1 => "BatchMode.Always",
                            2 => "BatchMode.Never",
                            _ => "BatchMode." + batchValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        };
                        diagnostics.Add(new DiagnosticInfo(
                            DescriptorId: "ZAO021",
                            Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                            MessageArgs: new EquatableArray<string>(ImmutableArray.Create(method.Name, batchName))));
                    }
                }
            }
        }

        var strategy = ResolveBatchStrategy(sql, batchMode);

        var (shape, nullableReaderMethod, materialization) = ClassifyEmitShape(method, conventionContext);

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
        if (shape == EmitShape.Unknown
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
                if (!isCt)
                {
                    var underlying = UnwrapNullableValueType(p.Type);
                    var resolution = ConventionDiscovery.Resolve(underlying, conventionContext);

                    // ZAO041 — no binding strategy resolved for parameter. Fires when the
                    // parameter type doesn't match any convention (no Value, no primitive,
                    // no enum, no static From factory, no single-arg ctor). Keyed at the
                    // parameter symbol's first declaration so the user's squiggle lands on
                    // their parameter, not on the type definition.
                    if (resolution.Kind == ConventionKind.Unknown)
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
                    paramConvention);
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
            MethodParameters: new EquatableArray<ParameterInfo>(methodParameters),
            CancellationTokenParameterName: cancellationTokenParameterName,
            Diagnostics: new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutable()));

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
    // returns concrete shapes only for the exact v0.1 Phase-4 templates with a
    // single CancellationToken parameter (no user-bound parameters). Everything
    // else stays Unknown and falls through to the stub-comment path until a later
    // Phase 4 task adds its template.
    //
    // For NullableScalar we also return the IDataReader.GetXxx method name to use
    // at emit time so EmitNullableScalar doesn't need to re-derive it from a model
    // that no longer carries the symbol.
    private static (EmitShape Shape, string? NullableReaderMethod, MaterializationModel? Materialization) ClassifyEmitShape(
        IMethodSymbol method,
        ConventionContext conventionContext)
    {
        if (method.ReturnType is not INamedTypeSymbol named) return (EmitShape.Unknown, null, null);
        // Restrict to Task<T> for now; ValueTask<T> lands later.
        if (!(named.Name == "Task" && named.Arity == 1)) return (EmitShape.Unknown, null, null);

        var inner = named.TypeArguments[0];

        // Scalar shapes now tolerate user parameters at the signature level — Phase 6
        // emits the binding loop so the SQL placeholders resolve at runtime. FlatRow
        // followed the same path in Task 5.1.

        // Nullable reference: Task<string?> (and any other supported nullable primitive).
        if (inner.IsReferenceType && inner.NullableAnnotation == NullableAnnotation.Annotated)
        {
            var readerMethod = GetScalarPrimitiveReaderInfo(inner);
            if (readerMethod is not null)
                return (EmitShape.NullableScalar, readerMethod, null);
        }

        // Nullable value type: Task<int?> / Task<Nullable<T>>.
        if (inner is INamedTypeSymbol innerNamed
            && innerNamed.IsGenericType
            && innerNamed.ConstructedFrom?.SpecialType == SpecialType.System_Nullable_T)
        {
            var underlying = innerNamed.TypeArguments[0];
            var readerMethod = GetScalarPrimitiveReaderInfo(underlying);
            if (readerMethod is not null)
                return (EmitShape.NullableScalar, readerMethod, null);
        }

        // Non-nullable int (Task 4.1) — kept as a separate shape so the existing
        // ExecuteScalarAsync emit + snapshot stays unchanged in v0.1.
        if (inner.SpecialType == SpecialType.System_Int32) return (EmitShape.ScalarInt, null, null);

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
            var flat = TryBuildFlatRowMaterialization(elementType, conventionContext);
            if (flat is not null)
                return (EmitShape.FlatRow, null, flat);

            // v0.2 Phase E — DomainEntity: a non-record class with a single public
            // ctor whose parameters all resolve to known conventions. The detection
            // sits AFTER FlatRow so record types continue to take the positional path
            // (record ctor params have synthesized properties on the type, making
            // them ambiguous between FlatRow-positional and DomainEntity-named; the
            // positional path stays the default for records).
            var domain = TryBuildDomainEntityMaterialization(elementType, conventionContext);
            if (domain is not null)
                return (EmitShape.DomainEntity, null, domain);
        }

        return (EmitShape.Unknown, null, null);
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
        ConventionContext conventionContext)
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
        var typeDisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        foreach (var p in ctor.Parameters)
        {
            // For nullable value types (`int?`) we strongly-type-read the underlying
            // primitive. For nullable reference types (`string?`) the reference type
            // IS the reader-target — string is read with GetString regardless of nullability.
            var underlying = UnwrapNullableValueType(p.Type);
            var resolution = ConventionDiscovery.Resolve(underlying, conventionContext);

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

            columns.Add(new ColumnBinding(
                GetterMethod: reader,
                IsNullable: isNullable,
                TypeName: p.Type.ToDisplayString(typeDisplayFormat),
                Convention: convention));
        }

        return new MaterializationModel(
            Kind: MaterializationKind.FlatRow,
            TargetTypeFullName: named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Columns: new EquatableArray<ColumnBinding>(columns.MoveToImmutable()));
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
        ConventionContext conventionContext)
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

        var typeDisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        var columns = ImmutableArray.CreateBuilder<ColumnBinding>(ctor.Parameters.Length);

        foreach (var p in ctor.Parameters)
        {
            var underlying = UnwrapNullableValueType(p.Type);
            var resolution = ConventionDiscovery.Resolve(underlying, conventionContext);

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

            columns.Add(new ColumnBinding(
                GetterMethod: reader,
                IsNullable: isNullable,
                TypeName: p.Type.ToDisplayString(typeDisplayFormat),
                Convention: convention,
                ColumnName: columnName));
        }

        return new MaterializationModel(
            Kind: MaterializationKind.DomainEntity,
            TargetTypeFullName: named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Columns: new EquatableArray<ColumnBinding>(columns.MoveToImmutable()));
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

    // Map an IDataReader.GetXxx reader-method name back to the C# integral type used
    // for the binding-cast (`(int)@status` etc.). The cast must target the enum's
    // underlying primitive, not the enum type, so the DbParameter sees an integer.
    private static string EnumUnderlyingCastTypeFromReader(string? readerMethod) => readerMethod switch
    {
        "GetInt32" => "int",
        "GetInt64" => "long",
        "GetInt16" => "short",
        "GetByte" => "byte",
        _ => "int", // safe default; non-integral readers never apply to enums
    };

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
        "ZAO021" => DiagnosticDescriptors.ZAO021_BatchNotImplemented,
        "ZAO022" => DiagnosticDescriptors.ZAO022_UnknownReturnShape,
        "ZAO040" => DiagnosticDescriptors.ZAO040_NoConstructionStrategy,
        "ZAO041" => DiagnosticDescriptors.ZAO041_NoUnwrapStrategy,
        "ZAO042" => DiagnosticDescriptors.ZAO042_StoreAsStringNonEnum,
        "ZAO043" => DiagnosticDescriptors.ZAO043_MaterializeFactoryMissing,
        "ZAO044" => DiagnosticDescriptors.ZAO044_AmbiguousDiscovery,
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
                default:
                    sb.AppendLine($"    // TODO: emit body for {m.MethodName} (uses {repo.ConnectionAccess}) -- v0.1 Task 4.x");
                    break;
            }
        }
        sb.AppendLine("}");

        var hint = $"{repo.ContainingTypeName}.g.cs";
        context.AddSource(hint, SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    // EF-style open-on-execute lifecycle: open if needed, single-command execute,
    // close-on-finally. Slot held only for ExecuteScalarAsync — minimum possible for
    // a single statement. Globally-qualified type names so emit composes regardless
    // of the consumer's `using` directives; `__`-prefixed locals avoid collision
    // with user parameter names; ConfigureAwait(false) consistently — library code.
    private static void EmitScalarInt(StringBuilder sb, QueryMethodModel m, string connectionAccess)
    {
        var sqlLiteral = SymbolDisplay.FormatLiteral(m.Sql, quote: true);
        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);
        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    public partial async global::System.Threading.Tasks.Task<int> {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        sb.AppendLine($"        var __conn = @{connectionAccess};");
        sb.AppendLine("        var __openedHere = __conn.State != global::System.Data.ConnectionState.Open;");
        sb.AppendLine($"        if (__openedHere) await __conn.OpenAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        sb.AppendLine($"            __cmd.CommandText = {sqlLiteral};");
        EmitParameterBinding(sb, m);
        sb.AppendLine($"            var __result = await __cmd.ExecuteScalarAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine("            return global::System.Convert.ToInt32(__result, global::System.Globalization.CultureInfo.InvariantCulture);");
        sb.AppendLine("        }");
        sb.AppendLine("        finally");
        sb.AppendLine("        {");
        sb.AppendLine("            if (__openedHere) await __conn.CloseAsync().ConfigureAwait(false);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
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
        var sqlLiteral = SymbolDisplay.FormatLiteral(m.Sql, quote: true);
        var readerMethod = m.NullableScalarReaderMethod ?? "GetValue";
        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);
        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    public partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        sb.AppendLine($"        var __conn = @{connectionAccess};");
        sb.AppendLine("        var __openedHere = __conn.State != global::System.Data.ConnectionState.Open;");
        sb.AppendLine($"        if (__openedHere) await __conn.OpenAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        sb.AppendLine($"            __cmd.CommandText = {sqlLiteral};");
        EmitParameterBinding(sb, m);
        sb.AppendLine($"            await using var __reader = await __cmd.ExecuteReaderAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine($"            if (!await __reader.ReadAsync({ct}).ConfigureAwait(false))");
        sb.AppendLine("                return null;");
        sb.AppendLine($"            return __reader.IsDBNull(0) ? null : __reader.{readerMethod}(0);");
        sb.AppendLine("        }");
        sb.AppendLine("        finally");
        sb.AppendLine("        {");
        sb.AppendLine("            if (__openedHere) await __conn.CloseAsync().ConfigureAwait(false);");
        sb.AppendLine("        }");
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

        var sqlLiteral = SymbolDisplay.FormatLiteral(m.Sql, quote: true);
        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);
        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    public partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        sb.AppendLine($"        var __conn = @{connectionAccess};");
        sb.AppendLine("        var __openedHere = __conn.State != global::System.Data.ConnectionState.Open;");
        sb.AppendLine($"        if (__openedHere) await __conn.OpenAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        sb.AppendLine($"            __cmd.CommandText = {sqlLiteral};");
        EmitParameterBinding(sb, m);
        sb.AppendLine($"            await using var __reader = await __cmd.ExecuteReaderAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine($"            if (!await __reader.ReadAsync({ct}).ConfigureAwait(false))");
        sb.AppendLine("                return null;");
        sb.AppendLine($"            return new {mat.TargetTypeFullName}(");
        var cols = mat.Columns;
        for (var i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            var trailing = i == cols.Length - 1 ? ");" : ",";
            // Primitive: direct GetXxx. Value-object / single-arg-ctor / static-factory:
            // wrap the primitive read in the discovered factory call (Phase C tasks
            // C.2/C.4/C.5). Nullable columns short-circuit to `null` via IsDBNull —
            // the cast carries the nullable type so the ternary types correctly.
            var readExpr = $"__reader.{col.GetterMethod}({i})";
            if (col.Convention is { } conv && conv.FactoryFullName is not null)
            {
                // Enum default-int: cast the underlying primitive to the enum type
                // (`(global::TestApp.OrderStatus)__reader.GetInt32(N)`).
                // EnumAsString: parse the string via `global::System.Enum.Parse<T>(...)`.
                // Both branches keep the inner read expression unchanged; only the
                // wrapper differs from the factory cases above.
                readExpr = conv.Kind switch
                {
                    (int)ConventionKind.Enum
                        => $"({conv.FactoryFullName}){readExpr}",
                    // AOT note: Enum.Parse<T> is annotated [RequiresUnreferencedCode]
                    // but is safe for closed enum types with a finite member set.
                    // v0.2 ships this baseline; v0.3+ can switch to a source-generated
                    // parse table if AOT-trim warnings bite a real consumer.
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
                expr = $"__reader.IsDBNull({i}) ? ({col.TypeName})null : {readExpr}";
            }
            else
            {
                expr = readExpr;
            }
            sb.AppendLine($"                {expr}{trailing}");
        }
        sb.AppendLine("        }");
        sb.AppendLine("        finally");
        sb.AppendLine("        {");
        sb.AppendLine("            if (__openedHere) await __conn.CloseAsync().ConfigureAwait(false);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
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

        var sqlLiteral = SymbolDisplay.FormatLiteral(m.Sql, quote: true);
        var paramList = BuildParameterList(m.MethodParameters);
        var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);
        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    public partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
        sb.AppendLine("    {");
        sb.AppendLine($"        var __conn = @{connectionAccess};");
        sb.AppendLine("        var __openedHere = __conn.State != global::System.Data.ConnectionState.Open;");
        sb.AppendLine($"        if (__openedHere) await __conn.OpenAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        sb.AppendLine($"            __cmd.CommandText = {sqlLiteral};");
        EmitParameterBinding(sb, m);
        sb.AppendLine($"            await using var __reader = await __cmd.ExecuteReaderAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine($"            if (!await __reader.ReadAsync({ct}).ConfigureAwait(false))");
        sb.AppendLine("                return null;");
        sb.AppendLine($"            return new {mat.TargetTypeFullName}(");
        var cols = mat.Columns;
        for (var i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            var trailing = i == cols.Length - 1 ? ");" : ",";
            // Column name comes from the ctor parameter (PascalCased). The literal
            // is passed verbatim into GetOrdinal; SQL's case-insensitive default
            // matching keeps `customerId` and `CustomerId` interchangeable on
            // SQLite / PostgreSQL / SQL Server.
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
            string expr;
            if (col.IsNullable)
            {
                expr = $"__reader.IsDBNull({ordinalExpr}) ? ({col.TypeName})null : {readExpr}";
            }
            else
            {
                expr = readExpr;
            }
            sb.AppendLine($"                {expr}{trailing}");
        }
        sb.AppendLine("        }");
        sb.AppendLine("        finally");
        sb.AppendLine("        {");
        sb.AppendLine("            if (__openedHere) await __conn.CloseAsync().ConfigureAwait(false);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
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
                    var castType = EnumUnderlyingCastTypeFromReader(conv.UnderlyingReader);
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
