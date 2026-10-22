using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

// IQuery<T>.Contains inside a predicate: an IN subquery. The form translated is
//
//   <chain>.Select(p => p.Col).Contains(<outer value>)
//
// where <chain> is a full Sql.Table<P>() chain with Where, ordering, Skip/Take and joins, read as
// a statement of its own with its own row and null knowledge. Its ordering and paging stay inside
// the subquery, so the children of a paged parent list filter on exactly the parents' ids.
//
// C#'s Contains never errs on a null, where SQL's NOT IN is NULL for every row once the subquery
// selects one NULL. So the selected column must be non-nullable; a filter `p.Col != null` in the
// subquery makes a nullable column eligible. The outer value may be null: over a set without
// NULL, `NULL IN (...)` is NULL, which a WHERE drops as C#'s false, and its negation takes IS NOT
// TRUE, true as in C#; see Negate. A subquery that reads the outer row would be correlated, ZAO102.
internal static partial class LinqQueryReader
{
    // `query.Select(p => p.Col).Contains(value)`: `value IN (SELECT "Col" ...)`. C#'s `!` over it
    // makes it NOT IN, or IS NOT TRUE for a value that can be null; see Negate.
    private static SqlInSubqueryExpression? ReadSubqueryContains(IInvocationOperation invocation, IParameterSymbol? row, ReaderContext context)
    {
        var subquery = ReadSubquery(ArgumentFor(invocation, 0), row, context);
        if (subquery is null) return null;

        var value = ArgumentFor(invocation, 1);
        var operand = ReadComparand(value, row, context);
        return operand is null ? null : new SqlInSubqueryExpression(operand, subquery, Negated: false);
    }

    // The subquery's chain, read with a context of its own: its lambdas' row, the values it knows
    // can be null and those lifted from an optional owned object are its own, and the outer
    // chain's come back afterwards. Companion parameters stay SQL parameters, as anywhere.
    private static SqlSelectStatement? ReadSubquery(IOperation source, IParameterSymbol? row, ReaderContext context)
    {
        var outerRow = context.Row;
        var outerNullable = new HashSet<SqlExpression>(context.NullableValues);
        var outerLifted = new HashSet<SqlExpression>(context.LiftedValues);
        context.NullableValues.Clear();
        context.LiftedValues.Clear();
        if (row is not null) context.OuterRows.Add(row);
        try
        {
            var chain = new ChainBuilder
            {
                IsSubquery = true,
                AliasPrefix = "s",
                Alias = HasJoin(source) ? "s0" : null,
            };
            return ReadChain(source, chain, context) ? BuildSubquery(source, chain, context) : null;
        }
        finally
        {
            if (row is not null) context.OuterRows.RemoveAt(context.OuterRows.Count - 1);
            context.Row = outerRow;
            context.NullableValues.Clear();
            context.NullableValues.UnionWith(outerNullable);
            context.LiftedValues.Clear();
            context.LiftedValues.UnionWith(outerLifted);
        }
    }

    private static SqlSelectStatement? BuildSubquery(IOperation source, ChainBuilder chain, ReaderContext context)
    {
        if (chain.TerminalOperator is { } terminal)
        {
            context.Report(LinqDiagnostics.Unsupported(
                "'" + terminal + "' in the source of an IQuery Contains, which selects one column,",
                Unwrap(source).Syntax.GetLocation()));
            return null;
        }
        if (chain.Projection is not { } projection || chain.SubqueryColumn is not { } selected)
        {
            context.Report(LinqDiagnostics.Unsupported(
                "'" + Unwrap(source).Syntax + "' as the source of an IQuery Contains, rather than a query ending in Select(x => x.Column),",
                Unwrap(source).Syntax.GetLocation()));
            return null;
        }

        // A NULL in the subquery makes IN NULL where C#'s Contains is true, and every NOT IN NULL.
        var column = (SqlMemberExpression)projection[0];
        if (MayBeNull(column, context.NullableValues) && !FiltersNotNull(chain, column))
        {
            var at = Unwrap(selected).Syntax;
            context.Report(LinqDiagnostics.Unsupported(
                "Selecting '" + at + "', which can be null, for an IQuery Contains without a '" + at + " != null' filter in the subquery,",
                at.GetLocation()));
            return null;
        }

        // ZAO110 as for a top-level chain: a page of parents in no defined order.
        if (chain.PagingLocation is { } paging && chain.Ordering.Count == 0)
            context.Report(LinqDiagnostics.UnorderedPaging(paging));

        return new SqlSelectStatement(
            chain.From!,
            new EquatableArray<SqlExpression>(projection),
            new EquatableArray<SqlExpression>(chain.Predicates.ToImmutable()),
            new EquatableArray<SqlOrdering>(chain.Ordering.ToImmutable()),
            new SqlPaging(chain.Limit, chain.Offset),
            default,
            JoinsOf(chain),
            chain.Alias);
    }

    // Select(p => p.Col) in a subquery: exactly one mapped column, which it selects.
    private static bool ReadSubquerySelect(
        IInvocationOperation invocation,
        IOperation returned,
        IParameterSymbol row,
        ChainBuilder chain,
        ReaderContext context)
    {
        if (chain.Group is not null)
            return Unsupported(invocation, "'GroupBy' in the source of an IQuery Contains", context);

        var value = UnwrapNullableCast(Unwrap(returned));
        var read = ReadOperand(value, row, context, out var column);
        if (read == OperandRead.Reported) return false;
        if (read != OperandRead.Read || column is not SqlMemberExpression)
        {
            context.Report(LinqDiagnostics.Unsupported(
                "'" + value.Syntax + "', rather than one mapped column, as what the source of an IQuery Contains selects,",
                value.Syntax.GetLocation()));
            return false;
        }

        chain.Projection = ImmutableArray.Create(column);
        chain.SubqueryColumn = value;
        return true;
    }

    // `(Guid?)t.Id`: the column itself. The cast only widens the selected type to a nullable one,
    // so that Contains takes a nullable outer value; the column still selects no NULL.
    private static IOperation UnwrapNullableCast(IOperation op)
        => op is IConversionOperation { Conversion: { IsNullable: true, IsUserDefined: false }, Operand: var operand }
            && op.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
                ? Unwrap(operand)
                : op;

    // Whether a condition of the subquery, in its WHERE or a join's ON, is `column IS NOT NULL`:
    // then the column selects no NULL, whatever its declared type.
    private static bool FiltersNotNull(ChainBuilder chain, SqlMemberExpression column)
    {
        foreach (var predicate in chain.Predicates)
        {
            if (IsNotNullTest(predicate, column)) return true;
        }
        foreach (var join in chain.Joins)
        {
            foreach (var predicate in join.SourcePredicates)
            {
                if (IsNotNullTest(predicate, column)) return true;
            }
        }
        return false;
    }

    // The same column, read at another place: members compare by name and source, not location.
    private static bool IsNotNullTest(SqlExpression predicate, SqlMemberExpression column)
        => predicate is SqlIsNullExpression { Negated: true, Operand: SqlMemberExpression tested }
            && string.Equals(tested.Member, column.Member, StringComparison.Ordinal)
            && string.Equals(tested.SourceAlias, column.SourceAlias, StringComparison.Ordinal);

    // A read of the row of a predicate an IN subquery is read inside: the row parameter itself,
    // or a member path from it, such as e.TaskId or e.Outcome.Url.
    private static bool ReadsOuterRow(IOperation op, ReaderContext context)
    {
        foreach (var outer in context.OuterRows)
        {
            var reads = op switch
            {
                IParameterReferenceOperation reference => IsParameter(reference, outer),
                IMemberReferenceOperation member => IsRooted(member.Instance, outer),
                _ => false,
            };
            if (reads) return true;
        }
        return false;
    }
}
