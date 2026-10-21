using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// Decides how each enum parameter without <c>[StoreAsString]</c> binds, from the columns it is
/// compared with: as its member name when they store names (<c>EnumAsString</c>), else as its
/// integer, the type's own convention. A parameter compared with both kinds of column cannot
/// bind one way for all of them, which is ZAO111.
/// </summary>
/// <remarks>
/// A <c>[StoreAsString]</c> enum is string-stored everywhere, so its parameters bind by name
/// already and never reach this decision. Only comparisons decide; a parameter that is only
/// tested for null, or only projected, keeps the type's convention.
/// </remarks>
internal static class EnumParameterStorage
{
    public static (ImmutableArray<string> ByName, ImmutableArray<DiagnosticInfo> Conflicts) Decide(
        SqlStatement statement,
        SqlNameScope names)
    {
        var votes = new Dictionary<string, bool>(StringComparer.Ordinal);
        var conflicted = new HashSet<string>(StringComparer.Ordinal);
        var conflicts = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        if (statement is SqlSelectStatement select)
        {
            foreach (var predicate in select.Predicates)
                Visit(predicate);
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
                    Vote(comparison.Left, comparison.Right);
                    Vote(comparison.Right, comparison.Left);
                    break;
                case SqlNotExpression not:
                    Visit(not.Operand);
                    break;
                case SqlConditionalExpression conditional:
                    Visit(conditional.Condition);
                    Visit(conditional.Predicate);
                    break;
            }
        }

        void Vote(SqlExpression side, SqlExpression other)
        {
            if (side is not SqlParameterExpression { ColumnPicksEnumStorage: true } parameter
                || other is not SqlMemberExpression member)
            {
                return;
            }

            var storesName = names.IsEnumAsString(member);
            if (!votes.TryGetValue(parameter.Name, out var first))
            {
                votes[parameter.Name] = storesName;
            }
            else if (first != storesName && conflicted.Add(parameter.Name))
            {
                conflicts.Add(LinqDiagnostics.EnumStorageConflict(parameter.Name, parameter.Location));
            }
        }
    }
}
