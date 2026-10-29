using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// The parameters a condition compares with a jsonb column. A column is jsonb when its map says
/// <c>Json = true</c>, or when <c>[Json]</c> marks its property or the record constructor
/// parameter that feeds it. Such a parameter binds as jsonb, as a <c>[Json]</c> parameter of a
/// <c>[Query]</c> does: left as text, PostgreSQL rejects <c>jsonb = text</c> at run time, 42883.
/// </summary>
/// <remarks>
/// jsonb equality compares the documents, not their text: <c>{"a":1}</c> equals <c>{ "a": 1 }</c>.
/// A parameter set into a jsonb column counts as a jsonb use too. It binds one way for the whole
/// method, so one that also meets a text column, sits in another form such as LIKE, or is set into
/// a text column is ZAO102. Checked when the statement is
/// bound, since only then is the map known.
/// </remarks>
internal static class JsonComparisons
{
    public static ImmutableArray<SqlParameterExpression> Check(
        ImmutableArray<(SqlStatement Statement, SqlNameScope Names)> statements,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var json = new List<SqlParameterExpression>();
        var other = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (statement, scope) in statements)
        {
            var names = scope;
            switch (statement)
            {
                case SqlSelectStatement select:
                    VisitSelect(select);
                    break;
                case SqlAggregateStatement aggregate:
                    VisitSelect(aggregate.Source);
                    break;
                case SqlScalarAggregateStatement scalar:
                    VisitSelect(scalar.Source);
                    break;
                case SqlDeleteStatement delete:
                    foreach (var predicate in delete.Predicates) Visit(predicate);
                    break;
                case SqlUpdateStatement update:
                    // A parameter set into a jsonb column binds as jsonb, as one compared with it does;
                    // set into any other column, it is a text one.
                    foreach (var setter in update.Setters)
                    {
                        if ((setter.ColumnJson || names.IsJson(setter.Column)) && setter.Value is SqlParameterExpression value)
                            json.Add(value);
                        else
                            Other(setter.Value);
                    }
                    foreach (var predicate in update.Predicates) Visit(predicate);
                    break;
                default:
                    throw new InvalidOperationException("No jsonb comparison walk for " + statement.GetType().Name + ".");
            }

            void VisitSelect(SqlSelectStatement select)
            {
                foreach (var item in select.Projection) Other(item);
                foreach (var join in select.Joins)
                {
                    Visit(join.On);
                    foreach (var predicate in join.SourcePredicates) Visit(predicate);
                }
                foreach (var predicate in select.Predicates) Visit(predicate);
                foreach (var key in select.GroupBy) Other(key);
                foreach (var ordering in select.Ordering) Other(ordering.Expression);
                if (select.Paging.Limit is { } limit) Other(limit);
                if (select.Paging.Offset is { } offset) Other(offset);
            }

            void Visit(SqlExpression expression)
            {
                switch (expression)
                {
                    case SqlBinaryExpression { Operator: SqlBinaryOperator.And or SqlBinaryOperator.Or } logical:
                        Visit(logical.Left);
                        Visit(logical.Right);
                        break;
                    case SqlBinaryExpression comparison:
                        Compare(comparison.Left, comparison.Right);
                        break;
                    case SqlRowComparisonExpression rows:
                        for (var i = 0; i < rows.Left.Length; i++) Compare(rows.Left[i], rows.Right[i]);
                        break;
                    // `p IS NULL` types nothing: it sits beside a comparison for C#'s null semantics.
                    case SqlIsNullExpression { Operand: SqlParameterExpression or SqlMemberExpression }:
                        break;
                    case SqlNotExpression not:
                        Visit(not.Operand);
                        break;
                    case SqlIsNotTrueExpression isNotTrue:
                        Visit(isNotTrue.Operand);
                        break;
                    case SqlConditionalExpression conditional:
                        Visit(conditional.Condition);
                        Visit(conditional.Predicate);
                        break;
                    case SqlInSubqueryExpression subquery:
                        Other(subquery.Operand);
                        var outer = names;
                        names = outer.Subquery(subquery.Subquery);
                        VisitSelect(subquery.Subquery);
                        names = outer;
                        break;
                    default:
                        Other(expression);
                        break;
                }
            }

            void Compare(SqlExpression left, SqlExpression right)
            {
                switch (left, right)
                {
                    case (SqlParameterExpression first, SqlMemberExpression firstMember) when IsJson(firstMember):
                        json.Add(first);
                        break;
                    case (SqlMemberExpression secondMember, SqlParameterExpression second) when IsJson(secondMember):
                        json.Add(second);
                        break;
                    default:
                        Other(left);
                        Other(right);
                        break;
                }
            }

            bool IsJson(SqlMemberExpression member) => member.Json || names.IsJson(member);
        }

        var bound = ImmutableArray.CreateBuilder<SqlParameterExpression>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in json)
        {
            if (!seen.Add(parameter.Name)) continue;
            if (other.Contains(parameter.Name))
            {
                diagnostics.Add(LinqDiagnostics.UnsupportedAt(
                    "Parameter '" + parameter.ClrName + "', which binds as jsonb for a jsonb column,"
                        + " used against something other than a jsonb column as well,",
                    parameter.Location));
            }
            bound.Add(parameter);
        }
        return bound.ToImmutable();

        void Other(SqlExpression expression) => other.UnionWith(SqlParameterReferences.Collect(new[] { expression }));
    }
}
