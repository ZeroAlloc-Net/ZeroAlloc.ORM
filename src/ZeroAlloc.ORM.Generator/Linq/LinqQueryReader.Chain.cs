using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

internal static partial class LinqQueryReader
{
    private const string SqlTypeName = "Sql";
    private const string QueryOperatorsTypeName = "QueryOperators";

    /// <summary>
    /// The parts of a SELECT the chain has filled so far, innermost operator first. An operator
    /// fills its part; the statement is built once the whole chain has been read.
    /// </summary>
    private sealed class ChainBuilder
    {
        public SqlTableSource? From { get; set; }

        public ImmutableArray<SqlExpression>? Projection { get; set; }

        public ImmutableArray<SqlExpression>.Builder Predicates { get; } = ImmutableArray.CreateBuilder<SqlExpression>();

        public ImmutableArray<SqlOrdering>.Builder Ordering { get; } = ImmutableArray.CreateBuilder<SqlOrdering>();

        public SqlExpression? Limit { get; set; }

        public SqlExpression? Offset { get; set; }

        // The last Skip or Take read, for the message when an operator follows it.
        public string? PagingOperator { get; set; }

        // The first Skip or Take, where ZAO110 is reported.
        public Location? PagingLocation { get; set; }

        // Count, LongCount or Any, which end the chain.
        public SqlAggregate? Terminal { get; set; }

        public string? TerminalOperator { get; set; }
    }

    private static SqlStatement? ReadStatement(ExpressionSyntax body, LinqResultShape shape, ReaderContext context)
    {
        var operation = context.Model.GetOperation(body, context.CancellationToken);
        var chain = new ChainBuilder();
        if (operation is null || !ReadChain(operation, chain, context)) return null;

        // ZAO110: the implicit LIMIT 1 below is not a Take, and a scalar shape does not depend on
        // the order, so only the row-list shapes warn.
        if (shape is LinqResultShape.List or LinqResultShape.Stream
            && chain.PagingLocation is { } paging
            && chain.Ordering.Count == 0)
        {
            context.Report(LinqDiagnostics.UnorderedPaging(paging));
        }

        // A terminal reads the chain's rows, without the projection: Select(...).Count() counts
        // the same rows as Count(). The ordering goes too; it changes neither a count nor an
        // existence, and PostgreSQL rejects ORDER BY next to a bare count(*).
        if (chain.Terminal is { } aggregate)
        {
            return new SqlAggregateStatement(aggregate, new SqlSelectStatement(
                chain.From!,
                default,
                new EquatableArray<SqlExpression>(chain.Predicates.ToImmutable()),
                default,
                new SqlPaging(chain.Limit, chain.Offset)));
        }

        if (chain.Projection is not { } projection)
        {
            context.Report(LinqDiagnostics.UnsupportedProjection(
                "the chain must end with Select(x => new Row(...)), constructing a positional record",
                body.GetLocation()));
            return null;
        }

        // The single-row shape reads the first row. A later Take sets Limit itself, which wins;
        // a check on Skip/Take without OrderBy must look at the chain, not at this implicit LIMIT.
        var limit = chain.Limit ?? (shape == LinqResultShape.SingleRow ? SqlLiteralExpression.Number("1") : null);
        return new SqlSelectStatement(
            chain.From!,
            new EquatableArray<SqlExpression>(projection),
            new EquatableArray<SqlExpression>(chain.Predicates.ToImmutable()),
            new EquatableArray<SqlOrdering>(chain.Ordering.ToImmutable()),
            new SqlPaging(limit, chain.Offset));
    }

    // Reads the chain source first, so operators apply in written order.
    private static bool ReadChain(IOperation operation, ChainBuilder chain, ReaderContext context)
    {
        var op = Unwrap(operation);
        if (op is not IInvocationOperation invocation)
        {
            context.Report(IsCapture(op, row: null)
                ? LinqDiagnostics.Capture(op.Syntax.ToString(), op.Syntax.GetLocation())
                : LinqDiagnostics.Unsupported("'" + op.Syntax + "' as the chain source", op.Syntax.GetLocation()));
            return false;
        }

        var target = invocation.TargetMethod;
        if (IsLinqMember(target, SqlTypeName, "Table"))
        {
            chain.From = new SqlTableSource(
                target.TypeArguments[0]
                    .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                    .ToDisplayString(OrmGenerator.TypeDisplayFormat),
                LocationInfo.From(invocation.Syntax.GetLocation()));
            return true;
        }
        if (!IsQueryOperator(target) || invocation.Arguments.Length == 0)
            return Unsupported(invocation, "'" + target.Name + "'", context);

        if (!ReadChain(invocation.Arguments[0].Value, chain, context)) return false;
        if (chain.TerminalOperator is { } terminal)
            return Unsupported(invocation, "'" + target.Name + "' after '" + terminal + "'", context);
        if (chain.Projection is not null && TerminalOf(target.Name) is null)
            return Unsupported(invocation, "'" + target.Name + "' after 'Select'", context);
        return ReadOperator(invocation, chain, context);
    }

    // One case per supported operator.
    private static bool ReadOperator(IInvocationOperation invocation, ChainBuilder chain, ReaderContext context)
    {
        var name = invocation.TargetMethod.Name;
        if (TerminalOf(name) is { } terminal)
        {
            chain.Terminal = terminal;
            chain.TerminalOperator = name;
            return true;
        }

        // SQL filters and orders before it pages. Written after Skip or Take, a filter or an
        // ordering would apply to the page alone, which one SELECT cannot express.
        if (chain.PagingOperator is { } paging && name is "Where" or "WhereIf" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending")
            return Unsupported(invocation, "'" + name + "' after '" + paging + "'", context);

        return name switch
        {
            "Select" => ReadSelect(invocation, chain, context),
            "Where" => ReadWhere(invocation, chain, context),
            "WhereIf" => ReadWhereIf(invocation, chain, context),
            "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" => ReadOrdering(invocation, chain, context),
            "Skip" or "Take" => ReadPaging(invocation, chain, context),
            _ => Unsupported(invocation, "'" + name + "'", context),
        };
    }

    private static SqlAggregate? TerminalOf(string operatorName) => operatorName switch
    {
        "Count" => SqlAggregate.Count,
        "LongCount" => SqlAggregate.LongCount,
        "Any" => SqlAggregate.Any,
        _ => null,
    };

    private static bool Unsupported(IInvocationOperation invocation, string construct, ReaderContext context)
    {
        context.Report(LinqDiagnostics.Unsupported(construct, OperatorLocation(invocation)));
        return false;
    }

    // `Where` in `source.Where(...)`; the whole call when it is not written as a member access.
    private static Location OperatorLocation(IInvocationOperation invocation)
        => invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access }
            ? access.Name.GetLocation()
            : invocation.Syntax.GetLocation();

    private static bool IsLinqMember(IMethodSymbol method, string typeName, string memberName)
        => string.Equals(method.Name, memberName, StringComparison.Ordinal) && IsLinqType(method, typeName);

    // Any operator of ZeroAlloc.ORM.Linq.QueryOperators; ReadOperator decides which are supported.
    private static bool IsQueryOperator(IMethodSymbol method) => IsLinqType(method, QueryOperatorsTypeName);

    private static bool IsLinqType(IMethodSymbol method, string typeName)
        => method.ContainingType is { } type
            && string.Equals(type.Name, typeName, StringComparison.Ordinal)
            && string.Equals(type.ContainingNamespace?.ToDisplayString(), LinqNamespace, StringComparison.Ordinal);

    // The lambda an operator takes, its one parameter, and the value it returns. Expression
    // lambdas and block lambdas with a single return statement both qualify.
    private static bool TryReadLambda(IOperation argument, out IParameterSymbol parameter, out IOperation returned)
    {
        parameter = null!;
        returned = null!;
        if (Unwrap(argument) is not IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda }
            || lambda.Symbol.Parameters.Length != 1
            || lambda.Body.Operations.Length != 1
            || lambda.Body.Operations[0] is not IReturnOperation { ReturnedValue: { } value })
        {
            return false;
        }
        parameter = lambda.Symbol.Parameters[0];
        returned = value;
        return true;
    }

    // Peels the conversions the compiler inserts without running user code: nullable wrapping,
    // numeric widening, reference conversions. A user-defined conversion runs code, so it stays.
    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation conversion
            && conversion.Conversion.IsImplicit
            && !conversion.Conversion.IsUserDefined)
        {
            operation = conversion.Operand;
        }
        return operation;
    }
}
