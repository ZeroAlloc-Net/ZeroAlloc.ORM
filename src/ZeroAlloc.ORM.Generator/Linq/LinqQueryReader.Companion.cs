using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

internal static partial class LinqQueryReader
{
    // The companion, its declaration, the chains its body is, and the T of each chain's IQuery<T>;
    // or the ZAO100 that rejects it. A companion returning IQuery<T> has one chain; one returning
    // a tuple of IQuery<T> has one per element, read from its tuple literal, and IsTuple set. A
    // [LinqCommand] companion returns the int of its one chain's write, which Write names.
    private readonly record struct Companion(
        IMethodSymbol? Symbol,
        MethodDeclarationSyntax? Syntax,
        ImmutableArray<ExpressionSyntax> Bodies,
        ImmutableArray<ITypeSymbol> RowTypes,
        bool IsTuple,
        DiagnosticInfo? Error,
        string? Write = null);

    // A tuple companion has 2 to 7 elements: one ValueTuple, without a nested TRest.
    private const int MaxTupleElements = 7;

    // ZAO100 — the companion is a static method of the same type, declared with a body that is
    // one expression or one return statement, returning IQuery<T>; or returning a tuple of 2 to 7
    // IQuery<T> with a tuple literal of one chain per element as that expression. A [LinqCommand]
    // companion's one chain ends in ExecuteDelete or ExecuteUpdate and it returns their int; a
    // [LinqQuery] companion's chain must not end in either.
    private static Companion ResolveCompanion(
        GeneratorAttributeSyntaxContext ctx,
        IMethodSymbol method,
        string name,
        bool isCommand,
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
        var write = WriteOf(SingleExpression(syntax), ctx.SemanticModel.Compilation, ct);
        if (isCommand)
            return ResolveCommandCompanion(name, method, symbol, syntax, write, identifier);
        if (write is { } written)
            return Invalid(name, method, "its chain is a write; use [LinqCommand]", written.Location);
        if (symbol.ReturnType is INamedTypeSymbol { IsTupleType: true } tuple)
            return ResolveTupleCompanion(name, method, symbol, syntax, tuple, identifier);
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
        return new Companion(symbol, syntax, ImmutableArray.Create(body), ImmutableArray.Create(rowType), false, null);
    }

    // A [LinqCommand] companion: one chain, ending in the write, returning the int the write gives.
    private static Companion ResolveCommandCompanion(
        string name,
        IMethodSymbol method,
        IMethodSymbol symbol,
        MethodDeclarationSyntax syntax,
        (string Operator, Location Location)? write,
        Location? identifier)
    {
        if (symbol.ReturnType is INamedTypeSymbol { IsTupleType: true })
            return Invalid(name, method, "it must be one chain ending in ExecuteDelete or ExecuteUpdate, not a tuple", syntax.ReturnType.GetLocation());
        if (SingleExpression(syntax) is not { } body)
        {
            return Invalid(
                name,
                method,
                "its body must be one expression or one return statement, from Sql.Table<T>() to ExecuteDelete or ExecuteUpdate",
                identifier);
        }
        if (write is not { } written)
            return Invalid(name, method, "its chain must end in ExecuteDelete or ExecuteUpdate", identifier);
        if (symbol.ReturnType.SpecialType != SpecialType.System_Int32)
            return Invalid(name, method, "it must return int, the number of rows its write affects", syntax.ReturnType.GetLocation());
        return new Companion(symbol, syntax, ImmutableArray.Create(body), ImmutableArray.Create(symbol.ReturnType), false, null, written.Operator);
    }

    // The write a chain ends in, ExecuteDelete or ExecuteUpdate, and where its name is; null for
    // any other body.
    private static (string Operator, Location Location)? WriteOf(ExpressionSyntax? body, Compilation compilation, CancellationToken ct)
    {
        if (body is null) return null;
        var operation = compilation.GetSemanticModel(body.SyntaxTree).GetOperation(body, ct);
        return operation is not null
            && Unwrap(operation) is IInvocationOperation invocation
            && IsQueryOperator(invocation.TargetMethod)
            && invocation.TargetMethod.Name is "ExecuteDelete" or "ExecuteUpdate"
                ? (invocation.TargetMethod.Name, OperatorLocation(invocation))
                : null;
    }

    // A tuple of IQuery<T>, whose body is a tuple literal with one complete chain per element.
    // Each chain is read as a companion body of its own; nothing else can be read without
    // running the companion.
    private static Companion ResolveTupleCompanion(
        string name,
        IMethodSymbol method,
        IMethodSymbol symbol,
        MethodDeclarationSyntax syntax,
        INamedTypeSymbol tuple,
        Location? identifier)
    {
        var elements = tuple.TupleElements;
        var rowTypes = ImmutableArray.CreateBuilder<ITypeSymbol>(elements.Length);
        foreach (var element in elements)
        {
            if (!IsGeneric(element.Type, LinqNamespace, "IQuery`1", out var rowType)) break;
            rowTypes.Add(rowType);
        }
        if (rowTypes.Count != elements.Length || elements.Length > MaxTupleElements)
        {
            return Invalid(
                name,
                method,
                "it must return IQuery<T>, or a tuple of 2 to 7 IQuery<T>, one per result set",
                syntax.ReturnType.GetLocation());
        }

        if (SingleExpression(syntax) is not TupleExpressionSyntax literal || literal.Arguments.Count != elements.Length)
        {
            return Invalid(
                name,
                method,
                "a tuple companion's body must be a tuple literal, (chain1, chain2, ...), with one chain from Sql.Table<T>() to its last operator per element",
                identifier);
        }
        var bodies = literal.Arguments.Select(a => a.Expression).ToImmutableArray();
        return new Companion(symbol, syntax, bodies, rowTypes.MoveToImmutable(), true, null);
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
        => new(null, null, default, default, false, LinqDiagnostics.InvalidCompanion(companion, method.Name, reason, location));
}
