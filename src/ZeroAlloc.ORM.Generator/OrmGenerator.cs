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

namespace ZeroAlloc.ORM.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class OrmGenerator : IIncrementalGenerator
{
    private const string QueryAttributeFullName = "ZeroAlloc.ORM.QueryAttribute";
    private const string IAsyncDbConnectionFullName = "System.Data.Async.IAsyncDbConnection";
    private const string IAsyncDbConnectionSimpleName = "IAsyncDbConnection";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var queryMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: QueryAttributeFullName,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => TransformMethod(ctx))
            .Where(static m => m is not null)!;

        var grouped = queryMethods.Collect()
            .SelectMany(static (methods, _) =>
                methods.GroupBy(m => m!.ContainingTypeFullName, StringComparer.Ordinal)
                       .Select(g =>
                       {
                           var first = g.First()!;
                           return new QueryRepositoryModel(
                               ContainingTypeFullName: g.Key,
                               ContainingTypeName: first.ContainingTypeName,
                               Namespace: first.Namespace,
                               Methods: new EquatableArray<QueryMethodModel>(g.Cast<QueryMethodModel>().ToImmutableArray()));
                       }));

        context.RegisterSourceOutput(grouped, (sourceCtx, repo) =>
        {
            var hadError = ReportDiagnostics(sourceCtx, repo);
            if (!hadError) EmitRepository(sourceCtx, repo);
        });
    }

    private static QueryMethodModel? TransformMethod(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol method) return null;
        if (method.ContainingType is not INamedTypeSymbol containing) return null;
        if (ctx.TargetNode is not MethodDeclarationSyntax methodSyntax) return null;

        var sql = ctx.Attributes
            .FirstOrDefault()?
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

        // ZAO005 — multiple ORM attributes on one method.
        // NOTE: This pipeline only triggers via [Query]. A method declared with ONLY
        // [Command] + [StoredProcedure] (no [Query]) would be invisible to us and ZAO005
        // would not fire. Once Phase 4 wires ForAttributeWithMetadataName for Command +
        // StoredProcedure, this limitation goes away.
        var ormAttrCount = method.GetAttributes()
            .Count(a => a.AttributeClass?.ToDisplayString() is
                "ZeroAlloc.ORM.QueryAttribute" or
                "ZeroAlloc.ORM.CommandAttribute" or
                "ZeroAlloc.ORM.StoredProcedureAttribute");
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
        if (CountStatements(sql) > 1 && !IsMultiResultReturnType(method.ReturnType))
        {
            diagnostics.Add(new DiagnosticInfo(
                DescriptorId: "ZAO008",
                Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                MessageArgs: new EquatableArray<string>(ImmutableArray.Create(method.Name))));
        }

        // ZAO007 — IAsyncEnumerable<T> requires a CT with [EnumeratorCancellation].
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
                diagnostics.Add(new DiagnosticInfo(
                    DescriptorId: "ZAO007",
                    Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
                    MessageArgs: new EquatableArray<string>(ImmutableArray.Create(method.Name))));
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

        var shape = ClassifyEmitShape(method);

        return new QueryMethodModel(
            MethodName: method.Name,
            ContainingTypeFullName: containing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ContainingTypeName: containing.Name,
            Namespace: containing.ContainingNamespace.IsGlobalNamespace
                ? null
                : containing.ContainingNamespace.ToDisplayString(),
            Sql: sql,
            ConnectionAccess: connectionAccess,
            ConnectionResolved: connectionResolved,
            ContainingTypePartial: containingTypePartial,
            ContainingTypeLocation: containingTypeLocation,
            Shape: shape,
            Diagnostics: new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutable()));
    }

    // Decide which emit template fits this method. Conservative on purpose:
    // returns ScalarInt only for the exact v0.1 Task 4.1 shape — `Task<int>` with
    // a single CancellationToken parameter (no user-bound parameters). Everything
    // else stays Unknown and falls through to the stub-comment path until a later
    // Phase 4 task adds its template.
    private static EmitShape ClassifyEmitShape(IMethodSymbol method)
    {
        if (method.ReturnType is not INamedTypeSymbol named) return EmitShape.Unknown;
        // Restrict to Task<T> for now; ValueTask<int> lands later.
        if (!(named.Name == "Task" && named.Arity == 1)) return EmitShape.Unknown;
        if (named.TypeArguments[0].SpecialType != SpecialType.System_Int32) return EmitShape.Unknown;

        // Only zero user parameters (CT-only). User-parameter binding is Phase 6.
        var userParamCount = method.Parameters.Count(p =>
            !string.Equals(p.Type.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal));
        if (userParamCount != 0) return EmitShape.Unknown;

        return EmitShape.ScalarInt;
    }

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

    // Naive `;`-count statement detector. Does NOT understand SQL string literals,
    // line comments (`--`), block comments (`/* */`), or PostgreSQL dollar-quoted
    // strings (`$tag$...$tag$`). For v0.1 this is acceptable — the most common case
    // (head + lines SELECT) works correctly. A proper tokeniser lands in v0.2.
    // TODO(v0.2): replace with a real SQL statement tokeniser.
    private static int CountStatements(string sql)
    {
        if (string.IsNullOrEmpty(sql)) return 0;
        var trimmed = sql.TrimEnd().TrimEnd(';');
        return trimmed.Count(c => c == ';') + 1;
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

        // Type-scoped diagnostics — emit once per repository, keyed at the containing type.
        // ZAO003 + ZAO004 are TYPE-scoped diagnostics but our model is per-method.
        // We store the type-properties (ConnectionResolved, ContainingTypePartial,
        // ContainingTypeLocation) as per-method redundancy and emit once via the
        // first method, because TransformMethod has no ordering info across siblings.
        // The grouping in Initialize guarantees one repo per containing type, so
        // "first method" semantics is well-defined. See TODO(v0.2) in QueryMethodModel
        // for the planned hoist into QueryRepositoryModel.
        var firstMethod = repo.Methods.Values.IsDefault || repo.Methods.Values.Length == 0
            ? null
            : repo.Methods.Values[0];
        if (firstMethod is not null && !firstMethod.ConnectionResolved)
        {
            hadError = true;
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ZAO003_NoConnection,
                firstMethod.ContainingTypeLocation?.ToLocation(),
                repo.ContainingTypeFullName));
        }
        if (firstMethod is not null && !firstMethod.ContainingTypePartial)
        {
            hadError = true;
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ZAO004_TypeNotPartial,
                firstMethod.ContainingTypeLocation?.ToLocation(),
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
                    EmitScalarInt(sb, m);
                    break;
                default:
                    sb.AppendLine($"    // TODO: emit body for {m.MethodName} (uses this.{m.ConnectionAccess}) -- v0.1 Task 4.x");
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
    private static void EmitScalarInt(StringBuilder sb, QueryMethodModel m)
    {
        var sqlLiteral = SymbolDisplay.FormatLiteral(m.Sql, quote: true);
        sb.AppendLine($"    public partial async global::System.Threading.Tasks.Task<int> {m.MethodName}(global::System.Threading.CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var __conn = this.{m.ConnectionAccess};");
        sb.AppendLine("        var __openedHere = __conn.State != global::System.Data.ConnectionState.Open;");
        sb.AppendLine("        if (__openedHere) await __conn.OpenAsync(ct).ConfigureAwait(false);");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
        sb.AppendLine($"            __cmd.CommandText = {sqlLiteral};");
        sb.AppendLine("            var __result = await __cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);");
        sb.AppendLine("            return global::System.Convert.ToInt32(__result, global::System.Globalization.CultureInfo.InvariantCulture);");
        sb.AppendLine("        }");
        sb.AppendLine("        finally");
        sb.AppendLine("        {");
        sb.AppendLine("            if (__openedHere) await __conn.CloseAsync().ConfigureAwait(false);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }
}
