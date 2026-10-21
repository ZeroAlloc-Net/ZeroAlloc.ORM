using System;
using System.Collections.Generic;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// The SQL names of the parameters a statement renders. A method parameter outside this set,
/// such as one only a dropped projection used, binds no DbParameter.
/// </summary>
internal static class SqlParameterReferences
{
    public static HashSet<string> Collect(SqlStatement statement)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        switch (statement)
        {
            case SqlSelectStatement select:
                AddSelect(select, names);
                break;
            case SqlAggregateStatement aggregate:
                AddSelect(aggregate.Source, names);
                break;
            default:
                throw new InvalidOperationException("No parameter walk for " + statement.GetType().Name + ".");
        }
        return names;
    }

    private static void AddSelect(SqlSelectStatement select, HashSet<string> names)
    {
        foreach (var item in select.Projection) Add(item, names);
        foreach (var predicate in select.Predicates) Add(predicate, names);
        foreach (var ordering in select.Ordering) Add(ordering.Expression, names);
        if (select.Paging.Limit is { } limit) Add(limit, names);
        if (select.Paging.Offset is { } offset) Add(offset, names);
    }

    // One case per SqlExpression record, as in the renderer.
    private static void Add(SqlExpression expression, HashSet<string> names)
    {
        switch (expression)
        {
            case SqlParameterExpression parameter:
                names.Add(parameter.Name);
                break;
            case SqlMemberExpression or SqlLiteralExpression or SqlEnumLiteralExpression:
                break;
            case SqlBinaryExpression binary:
                Add(binary.Left, names);
                Add(binary.Right, names);
                break;
            case SqlRowComparisonExpression rows:
                foreach (var item in rows.Left) Add(item, names);
                foreach (var item in rows.Right) Add(item, names);
                break;
            case SqlNotExpression not:
                Add(not.Operand, names);
                break;
            case SqlIsNullExpression isNull:
                Add(isNull.Operand, names);
                break;
            case SqlLikeExpression like:
                Add(like.Operand, names);
                Add(like.Value, names);
                break;
            case SqlILikeExpression ilike:
                Add(ilike.Operand, names);
                Add(ilike.Pattern, names);
                break;
            case SqlAnyExpression any:
                Add(any.Operand, names);
                Add(any.Array, names);
                break;
            case SqlConditionalExpression conditional:
                Add(conditional.Condition, names);
                Add(conditional.Predicate, names);
                break;
            case SqlPagingCountExpression count:
                Add(count.Count, names);
                break;
            default:
                throw new InvalidOperationException("No parameter walk for " + expression.GetType().Name + ".");
        }
    }
}
