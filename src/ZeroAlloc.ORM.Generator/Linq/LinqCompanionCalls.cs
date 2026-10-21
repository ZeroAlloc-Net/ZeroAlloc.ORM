using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>A companion some <c>[LinqQuery]</c> names: its type, its name and the method it serves.</summary>
internal sealed record LinqCompanionRef(string TypeName, string Companion, string Method);

/// <summary>An invocation, by the simple name it calls; nothing about it is resolved yet.</summary>
internal sealed record InvocationCandidate(string Name, LocationInfo Location);

/// <summary>
/// ZAO109 — a call to a companion. Every invocation is read by its simple name only, which
/// needs no semantic model. A name that no companion has is dropped there, so symbols are
/// resolved only for the few calls whose name a companion shares.
/// </summary>
internal static class LinqCompanionCalls
{
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    public static LinqCompanionRef? ReadCompanion(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol method
            || ctx.Attributes[0].ConstructorArguments.FirstOrDefault().Value is not string { Length: > 0 } companion)
        {
            return null;
        }
        return new LinqCompanionRef(method.ContainingType.OriginalDefinition.ToDisplayString(TypeFormat), companion, method.Name);
    }

    public static EquatableArray<LinqCompanionRef> Sort(ImmutableArray<LinqCompanionRef?> companions)
        => new(companions
            .OfType<LinqCompanionRef>()
            .OrderBy(c => c.TypeName, StringComparer.Ordinal)
            .ThenBy(c => c.Companion, StringComparer.Ordinal)
            .ThenBy(c => c.Method, StringComparer.Ordinal)
            .ToImmutableArray());

    public static bool IsNamedInvocation(SyntaxNode node)
        => node is InvocationExpressionSyntax invocation && InvokedName(invocation) is not null;

    public static InvocationCandidate ReadInvocation(SyntaxNode node)
    {
        var invocation = (InvocationExpressionSyntax)node;
        return new InvocationCandidate(InvokedName(invocation)!, new LocationInfo(node.SyntaxTree, invocation.Span));
    }

    public static bool SharesAName(InvocationCandidate call, EquatableArray<LinqCompanionRef> companions)
        => companions.Any(c => string.Equals(c.Companion, call.Name, StringComparison.Ordinal));

    // The one step that binds: the call's symbol, against the companions of that name.
    public static DiagnosticInfo? Resolve(
        InvocationCandidate call,
        EquatableArray<LinqCompanionRef> companions,
        Compilation compilation,
        CancellationToken ct)
    {
        var tree = call.Location.Tree;
        if (!compilation.ContainsSyntaxTree(tree)) return null;
        if (tree.GetRoot(ct).FindNode(call.Location.Span, getInnermostNodeForTie: true) is not InvocationExpressionSyntax invocation)
            return null;

        var info = compilation.GetSemanticModel(tree).GetSymbolInfo(invocation, ct);
        // A call with the wrong arguments still names the companion: it is not overloaded.
        var target = info.Symbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null);
        if (target is not IMethodSymbol method) return null;
        method = method.ReducedFrom ?? method;

        var typeName = method.ContainingType.OriginalDefinition.ToDisplayString(TypeFormat);
        foreach (var companion in companions)
        {
            if (string.Equals(companion.Companion, method.Name, StringComparison.Ordinal)
                && string.Equals(companion.TypeName, typeName, StringComparison.Ordinal))
            {
                return LinqDiagnostics.CompanionCalled(companion.Companion, companion.Method, call.Location);
            }
        }
        return null;
    }

    private static string? InvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        SimpleNameSyntax name => name.Identifier.ValueText,
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
        _ => null,
    };
}
