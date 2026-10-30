using System.Collections.Immutable;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// An UPDATE's setters into jsonb columns. A column is jsonb when its map says <c>Json = true</c>,
/// or when <c>[Json]</c> marks its property or the record constructor parameter that feeds it, so
/// a domain class needs no attribute. Checked when the statement is bound, since only then is the
/// map known: the ZAO102s.
/// </summary>
internal static class JsonSetters
{
    /// <summary>
    /// Adds to <paramref name="diagnostics"/> any setter of <paramref name="update"/> PostgreSQL would
    /// reject. The parameters set into jsonb columns bind as jsonb, and a parameter that also meets
    /// a non-jsonb use is reported, in <see cref="ParameterStoreTypes"/>.
    /// </summary>
    public static void Check(
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
    }
}
