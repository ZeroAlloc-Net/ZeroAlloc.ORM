using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// Decides how each enum parameter without <c>[StoreAsString]</c> binds, from what it is compared
/// with: as its member name when the columns store names (<c>EnumAsString</c>), else as its
/// integer, the type's own convention. Runs before the statement is rendered, so an enum
/// constant compared with a name-bound parameter renders as a name too.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A parameter compared with both kinds of column cannot bind one way for all, ZAO111.</item>
/// <item>Two parameters compared with each other bind the same way: a column-decided one decides
/// the other, and two decided differently are ZAO111.</item>
/// <item>Two columns compared with each other that store the enum differently would compare text
/// with an integer, ZAO102.</item>
/// </list>
/// A <c>[StoreAsString]</c> enum is string-stored everywhere, so it never reaches this decision.
/// A parameter that is only tested for null, or only projected, keeps the type's convention.
/// </remarks>
internal static class EnumParameterStorage
{
    public static (ImmutableArray<string> ByName, ImmutableArray<DiagnosticInfo> Conflicts) Decide(
        SqlStatement statement,
        SqlNameScope names)
    {
        var votes = new Dictionary<string, bool>(StringComparer.Ordinal);
        var links = new List<(SqlParameterExpression Left, SqlParameterExpression Right)>();
        var conflicted = new HashSet<string>(StringComparer.Ordinal);
        var conflicts = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var select = statement switch
        {
            SqlSelectStatement own => own,
            SqlAggregateStatement aggregate => aggregate.Source,
            SqlScalarAggregateStatement scalar => scalar.Source,
            _ => throw new InvalidOperationException("No enum storage walk for " + statement.GetType().Name + "."),
        };
        foreach (var join in select.Joins)
        {
            Visit(join.On);
            foreach (var predicate in join.SourcePredicates)
                Visit(predicate);
        }
        foreach (var predicate in select.Predicates)
            Visit(predicate);

        // A parameter compared with a decided one binds the same way.
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (left, right) in links)
            {
                var leftDecided = votes.TryGetValue(left.Name, out var leftByName);
                var rightDecided = votes.TryGetValue(right.Name, out var rightByName);
                if (leftDecided && !rightDecided)
                {
                    votes[right.Name] = leftByName;
                    changed = true;
                }
                else if (rightDecided && !leftDecided)
                {
                    votes[left.Name] = rightByName;
                    changed = true;
                }
                else if (leftDecided && leftByName != rightByName)
                {
                    Conflict(right);
                }
            }
        }

        var byName = ImmutableArray.CreateBuilder<string>();
        foreach (var vote in votes)
        {
            if (vote.Value && !conflicted.Contains(vote.Key)) byName.Add(vote.Key);
        }
        byName.Sort(StringComparer.Ordinal);
        return (byName.ToImmutable(), conflicts.ToImmutable());

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
                    for (var i = 0; i < rows.Left.Length; i++)
                        Compare(rows.Left[i], rows.Right[i]);
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
                // Forms that compare no enum parameter with a column or another parameter. One
                // case per SqlExpression record, as in the renderer, so a new form is decided here.
                case SqlMemberExpression or SqlParameterExpression or SqlLiteralExpression or SqlEnumLiteralExpression
                    or SqlIsNullExpression or SqlCastExpression or SqlLikeExpression or SqlILikeExpression
                    or SqlAnyExpression or SqlPagingCountExpression or SqlAggregateCallExpression
                    or SqlDateTruncExpression:
                    break;
                default:
                    throw new InvalidOperationException("No enum storage walk for " + expression.GetType().Name + ".");
            }
        }

        void Compare(SqlExpression left, SqlExpression right)
        {
            switch (left, right)
            {
                case (SqlParameterExpression { ColumnPicksEnumStorage: true } parameter, SqlMemberExpression member):
                    Vote(parameter, names.IsEnumAsString(member));
                    break;
                case (SqlMemberExpression member, SqlParameterExpression { ColumnPicksEnumStorage: true } parameter):
                    Vote(parameter, names.IsEnumAsString(member));
                    break;
                case (SqlParameterExpression { ColumnPicksEnumStorage: true } first, SqlParameterExpression { ColumnPicksEnumStorage: true } second):
                    links.Add((first, second));
                    break;
                case (SqlMemberExpression { ColumnPicksEnumStorage: true } first, SqlMemberExpression { ColumnPicksEnumStorage: true } second)
                    when names.IsEnumAsString(first) != names.IsEnumAsString(second):
                    conflicts.Add(LinqDiagnostics.UnsupportedAt(
                        "Comparing '" + first.Member + "' with '" + second.Member
                            + "', whose columns store the enum differently, one by name and one as an integer,",
                        first.Location));
                    break;
            }
        }

        void Vote(SqlParameterExpression parameter, bool storesName)
        {
            if (!votes.TryGetValue(parameter.Name, out var first))
                votes[parameter.Name] = storesName;
            else if (first != storesName)
                Conflict(parameter);
        }

        void Conflict(SqlParameterExpression parameter)
        {
            if (conflicted.Add(parameter.Name))
                conflicts.Add(LinqDiagnostics.EnumStorageConflict(parameter.ClrName, parameter.Location));
        }
    }
}
