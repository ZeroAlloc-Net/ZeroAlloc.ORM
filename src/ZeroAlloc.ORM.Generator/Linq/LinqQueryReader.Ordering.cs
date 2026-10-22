using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.ORM.Generator.Linq;

internal static partial class LinqQueryReader
{
    // OrderBy, ThenBy and their Descending forms: one ORDER BY key each, a mapped column. A
    // column stored by name orders by the stored text, as documented; that is not translated.
    // A second OrderBy would make its key the primary one in LINQ, so ThenBy is the only way
    // to add a key.
    private static bool ReadOrdering(IInvocationOperation invocation, ChainBuilder chain, ReaderContext context)
    {
        var name = invocation.TargetMethod.Name;
        var startsOrder = name.StartsWith("OrderBy", StringComparison.Ordinal);
        if (startsOrder && chain.Ordering.Count > 0)
            return Unsupported(invocation, "'" + name + "' after 'OrderBy'", context);
        if (invocation.Arguments.Length != 2
            || !TryReadLambda(invocation.Arguments[1].Value, out var row, out var returned))
        {
            return Unsupported(invocation, "'" + name + "' without a lambda", context);
        }

        if (chain.Group is { } group)
            return ReadGroupOrdering(name, returned, row, group, chain, context);

        if (IsXminCall(Unwrap(returned)))
        {
            context.Report(LinqDiagnostics.Unsupported(
                "'" + Unwrap(returned).Syntax + "' in an ordering, " + XminReason, Unwrap(returned).Syntax.GetLocation()));
            return false;
        }

        var key = ReadComparand(returned, row, context);
        if (key is null) return false;
        if (key is not SqlMemberExpression)
        {
            // Ordering by a parameter or a constant orders nothing, and an integer constant in
            // ORDER BY names an output column in PostgreSQL.
            ReportUnsupported(Unwrap(returned), context);
            return false;
        }

        chain.Ordering.Add(new SqlOrdering(key, name.EndsWith("Descending", StringComparison.Ordinal), MayBeNull(key, context.NullableValues)));
        return true;
    }

    // An ordering of groups: by g.Key, a key member or a group aggregate, as ORDER BY over the
    // key's expression or the aggregate call.
    private static bool ReadGroupOrdering(
        string name,
        IOperation returned,
        IParameterSymbol group,
        GroupKey key,
        ChainBuilder chain,
        ReaderContext context)
    {
        switch (ReadGroupValue(returned, group, key, context, out var expression))
        {
            case OperandRead.Read:
                chain.Ordering.Add(new SqlOrdering(expression!, name.EndsWith("Descending", StringComparison.Ordinal), MayBeNull(expression!, context.NullableValues)));
                return true;
            case OperandRead.Reported:
                return false;
            default:
                ReportUnsupported(Unwrap(returned), context);
                return false;
        }
    }

    // Skip(n) and Take(n): OFFSET and LIMIT, each at most once, with Skip first, so the SQL
    // reads them in the order the chain applies them. n is a companion parameter, bound with
    // its Int32 facet and clamped at 0 as LINQ does, or a non-negative int literal, since a
    // negative literal is surely a mistake.
    private static bool ReadPaging(IInvocationOperation invocation, ChainBuilder chain, ReaderContext context)
    {
        var name = invocation.TargetMethod.Name;
        var isTake = string.Equals(name, "Take", StringComparison.Ordinal);
        if (chain.Limit is not null || (!isTake && chain.Offset is not null))
            return Unsupported(invocation, "'" + name + "' after '" + chain.PagingOperator + "'", context);
        if (invocation.Arguments.Length != 2) return Unsupported(invocation, "'" + name + "'", context);

        var argument = ArgumentFor(invocation, 1);
        var count = ReadComparand(argument, row: null, context);
        if (count is null) return false;
        if (count is not (SqlParameterExpression or SqlLiteralExpression { Kind: SqlLiteralKind.Number })
            || (count is SqlLiteralExpression literal && literal.Value.StartsWith("-", StringComparison.Ordinal)))
        {
            ReportUnsupported(Unwrap(argument), context);
            return false;
        }

        // A literal is non-negative already; a parameter is clamped when rendered.
        if (count is SqlParameterExpression) count = new SqlPagingCountExpression(count, AtMostOne: false);
        if (isTake) chain.Limit = count;
        else chain.Offset = count;
        chain.PagingOperator = name;
        chain.PagingLocation ??= OperatorLocation(invocation);
        return true;
    }
}
