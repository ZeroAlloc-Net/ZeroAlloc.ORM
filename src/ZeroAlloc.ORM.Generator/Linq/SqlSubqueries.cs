using System;
using System.Collections.Generic;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// The IN subqueries a statement holds, nested ones included, outermost first. The binder checks
/// their sources against the maps before the render, so an unmapped entity in a subquery fails
/// the query with one ZAO104, as it does at the top level, rather than rendering on.
/// </summary>
internal static class SqlSubqueries
{
    public static List<SqlSelectStatement> Collect(SqlStatement statement)
    {
        var subqueries = new List<SqlSelectStatement>();
        switch (statement)
        {
            case SqlSelectStatement select:
                AddSelect(select, subqueries);
                break;
            case SqlAggregateStatement aggregate:
                AddSelect(aggregate.Source, subqueries);
                break;
            case SqlScalarAggregateStatement scalar:
                Add(scalar.Call, subqueries);
                AddSelect(scalar.Source, subqueries);
                break;
            default:
                throw new InvalidOperationException("No subquery walk for " + statement.GetType().Name + ".");
        }
        return subqueries;
    }

    private static void AddSelect(SqlSelectStatement select, List<SqlSelectStatement> subqueries)
    {
        foreach (var item in select.Projection) Add(item, subqueries);
        foreach (var join in select.Joins)
        {
            Add(join.On, subqueries);
            foreach (var predicate in join.SourcePredicates) Add(predicate, subqueries);
        }
        foreach (var predicate in select.Predicates) Add(predicate, subqueries);
        foreach (var key in select.GroupBy) Add(key, subqueries);
        foreach (var ordering in select.Ordering) Add(ordering.Expression, subqueries);
        if (select.Paging.Limit is { } limit) Add(limit, subqueries);
        if (select.Paging.Offset is { } offset) Add(offset, subqueries);
    }

    // One case per SqlExpression record, as in the renderer.
    private static void Add(SqlExpression expression, List<SqlSelectStatement> subqueries)
    {
        switch (expression)
        {
            case SqlInSubqueryExpression subquery:
                Add(subquery.Operand, subqueries);
                subqueries.Add(subquery.Subquery);
                AddSelect(subquery.Subquery, subqueries);
                break;
            case SqlMemberExpression or SqlParameterExpression or SqlLiteralExpression or SqlEnumLiteralExpression
                or SqlOwnedIsNullExpression:
                break;
            case SqlBinaryExpression binary:
                Add(binary.Left, subqueries);
                Add(binary.Right, subqueries);
                break;
            case SqlRowComparisonExpression rows:
                foreach (var item in rows.Left) Add(item, subqueries);
                foreach (var item in rows.Right) Add(item, subqueries);
                break;
            case SqlNotExpression not:
                Add(not.Operand, subqueries);
                break;
            case SqlIsNotTrueExpression isNotTrue:
                Add(isNotTrue.Operand, subqueries);
                break;
            case SqlIsNullExpression isNull:
                Add(isNull.Operand, subqueries);
                break;
            case SqlCastExpression cast:
                Add(cast.Operand, subqueries);
                break;
            case SqlLikeExpression like:
                Add(like.Operand, subqueries);
                Add(like.Value, subqueries);
                break;
            case SqlILikeExpression ilike:
                Add(ilike.Operand, subqueries);
                Add(ilike.Pattern, subqueries);
                break;
            case SqlAnyExpression any:
                Add(any.Operand, subqueries);
                Add(any.Array, subqueries);
                break;
            case SqlConditionalExpression conditional:
                Add(conditional.Condition, subqueries);
                Add(conditional.Predicate, subqueries);
                break;
            case SqlPagingCountExpression count:
                Add(count.Count, subqueries);
                break;
            case SqlAggregateCallExpression call:
                if (call.Argument is { } argument) Add(argument, subqueries);
                break;
            case SqlDateTruncExpression dateTrunc:
                Add(dateTrunc.Operand, subqueries);
                break;
            default:
                throw new InvalidOperationException("No subquery walk for " + expression.GetType().Name + ".");
        }
    }
}
