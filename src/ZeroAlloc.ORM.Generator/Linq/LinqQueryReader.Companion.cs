using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

internal static partial class LinqQueryReader
{
    // The companion, its declaration, the one expression its body is, and the T of its IQuery<T>;
    // or the ZAO100 that rejects it.
    private readonly record struct Companion(
        IMethodSymbol? Symbol,
        MethodDeclarationSyntax? Syntax,
        ExpressionSyntax? Body,
        ITypeSymbol? RowType,
        DiagnosticInfo? Error);

    // ZAO100 — the companion is a static method of the same type, declared with a body that is
    // one expression or one return statement, returning IQuery<T>.
    private static Companion ResolveCompanion(
        GeneratorAttributeSyntaxContext ctx,
        IMethodSymbol method,
        string name,
        CancellationToken ct)
    {
        var candidates = method.ContainingType.GetMembers(name)
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .ToList();
        if (candidates.Count != 1)
        {
            var reason = candidates.Count == 0
                ? "it was not found in '" + method.ContainingType.Name + "'"
                : "it is overloaded; give the companion a name of its own";
            return Invalid(name, method, reason, CompanionArgumentLocation(ctx, ct));
        }

        var symbol = candidates[0];
        var syntax = symbol.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax(ct))
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Body is not null || m.ExpressionBody is not null);
        var identifier = syntax?.Identifier.GetLocation() ?? symbol.Locations.FirstOrDefault();
        if (!symbol.IsStatic)
            return Invalid(name, method, "it must be static", identifier);
        if (syntax is null)
            return Invalid(name, method, "it has no body to read", identifier);
        if (!IsGeneric(symbol.ReturnType, LinqNamespace, "IQuery`1", out var rowType))
            return Invalid(name, method, "it must return IQuery<T>", syntax.ReturnType.GetLocation());

        var body = SingleExpression(syntax);
        if (body is null)
        {
            return Invalid(
                name,
                method,
                "its body must be one expression or one return statement, from Sql.Table<T>() to the last operator",
                identifier);
        }
        return new Companion(symbol, syntax, body, rowType, null);
    }

    private static ExpressionSyntax? SingleExpression(MethodDeclarationSyntax syntax)
    {
        if (syntax.ExpressionBody is { } arrow) return arrow.Expression;
        return syntax.Body is { Statements.Count: 1 } block
            && block.Statements[0] is ReturnStatementSyntax { Expression: { } returned }
                ? returned
                : null;
    }

    // The attribute's argument, `nameof(Companion)` or a string, where a missing companion is reported.
    private static Location? CompanionArgumentLocation(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var attribute = ctx.Attributes[0].ApplicationSyntaxReference?.GetSyntax(ct) as AttributeSyntax;
        return attribute?.ArgumentList?.Arguments.FirstOrDefault()?.Expression.GetLocation()
            ?? attribute?.GetLocation();
    }

    private static Companion Invalid(string companion, IMethodSymbol method, string reason, Location? location)
        => new(null, null, null, null, LinqDiagnostics.InvalidCompanion(companion, method.Name, reason, location));
}
