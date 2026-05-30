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
            Diagnostics: new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutable()));
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

    private static bool IsIAsyncEnumerable(ITypeSymbol returnType)
    {
        var arity = (returnType as INamedTypeSymbol)?.Arity ?? 0;
        return string.Equals(returnType.Name, "IAsyncEnumerable", StringComparison.Ordinal) && arity == 1;
    }

    private static bool IsIAsyncDbConnection(ITypeSymbol type)
    {
        // The v0.1 abstraction package doesn't define IAsyncDbConnection yet (it lands in
        // a later phase), so the test compilations see it as an unresolved error type
        // whose ToDisplayString() yields the simple name. In real consumer code the
        // namespace will be resolvable.
        var display = type.ToDisplayString();
        if (string.Equals(display, IAsyncDbConnectionFullName, StringComparison.Ordinal))
            return true;
        if (string.Equals(display, IAsyncDbConnectionSimpleName, StringComparison.Ordinal))
        {
            // Error-type case (Abstractions doesn't ship IAsyncDbConnection yet — comes from AdoNet.Async).
            // Accept only when namespace is empty (genuine error type) OR matches the expected namespace.
            var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            return ns is "" or "<global namespace>" or "System.Data.Async";
        }
        // type.Name fallback removed — too permissive (would match user-defined IAsyncDbConnection
        // in any namespace).
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
        foreach (var m in repo.Methods)
        {
            sb.AppendLine($"    // TODO: emit body for {m.MethodName} (uses this.{m.ConnectionAccess}) -- v0.1 Task 4.x");
        }
        sb.AppendLine("}");

        var hint = $"{repo.ContainingTypeName}.g.cs";
        context.AddSource(hint, SourceText.From(sb.ToString(), Encoding.UTF8));
    }
}
