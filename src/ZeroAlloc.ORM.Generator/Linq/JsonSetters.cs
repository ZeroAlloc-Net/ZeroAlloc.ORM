using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// An UPDATE's setters into jsonb columns. A column is jsonb when its map says <c>Json = true</c>,
/// or when <c>[Json]</c> marks its property or the record constructor parameter that feeds it, so
/// a domain class needs no attribute. Checked when the statement is bound, since only then is the
/// map known: the parameters set into those columns, which bind as jsonb, and the ZAO102s.
/// </summary>
internal static class JsonSetters
{
    /// <summary>
    /// The parameters <paramref name="update"/> sets into jsonb columns, each once at its first such
    /// setter, after adding to <paramref name="diagnostics"/> any setter PostgreSQL would reject.
    /// </summary>
    public static ImmutableArray<SqlParameterExpression> Check(
        SqlUpdateStatement update,
        SqlNameScope names,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var json = new bool[update.Setters.Length];
        for (var i = 0; i < json.Length; i++)
            json[i] = update.Setters[i].ColumnJson || names.IsJson(update.Setters[i].Column);

        // A jsonb column takes a document: a parameter, which binds as jsonb, a constant or null,
        // which PostgreSQL types from the column, or another jsonb column. A text column's value
        // would be rejected at run time, 42804.
        for (var i = 0; i < json.Length; i++)
        {
            var setter = update.Setters[i];
            if (json[i] && setter.Value is SqlMemberExpression member && !setter.ValueJson && !names.IsJson(member))
            {
                diagnostics.Add(LinqDiagnostics.UnsupportedAt(
                    "'" + member.Member + "' set into the jsonb '" + setter.Column.Member
                        + "', rather than a parameter, a constant, null or another jsonb column,",
                    member.Location));
            }
        }

        // A parameter set into a jsonb column binds as jsonb wherever the statement names it. Named
        // in a condition or in another setter too, it would meet a text or other column there as
        // jsonb, which PostgreSQL rejects at run time.
        //
        // Conservative on purpose: a condition that compares the parameter with another jsonb
        // column, `t.Snapshot == doc`, would bind as jsonb consistently and work, yet is rejected
        // too. Telling it apart means knowing every column each use meets, nested subqueries and
        // WhereIf included, which a jsonb document compared for equality does not justify. A
        // parameter that only meets jsonb columns in conditions is bound by JsonComparisons.
        var elsewhere = SqlParameterReferences.Collect(update.Predicates);
        for (var i = 0; i < json.Length; i++)
        {
            if (!json[i]) elsewhere.UnionWith(SqlParameterReferences.Collect(new[] { update.Setters[i].Value }));
        }

        var bound = ImmutableArray.CreateBuilder<SqlParameterExpression>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < json.Length; i++)
        {
            if (!json[i] || update.Setters[i].Value is not SqlParameterExpression parameter || !seen.Add(parameter.Name))
                continue;
            if (elsewhere.Contains(parameter.Name))
            {
                diagnostics.Add(LinqDiagnostics.UnsupportedAt(
                    "Parameter '" + parameter.ClrName + "', which binds as jsonb for the jsonb '" + update.Setters[i].Column.Member
                        + "', used in a condition or another setter as well,",
                    parameter.Location));
            }
            bound.Add(parameter);
        }
        return bound.ToImmutable();
    }
}
