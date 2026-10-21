using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

// The SQL a [LinqQuery] chain translates to, before any dialect renders it. Records only, holding
// strings, enums, locations and EquatableArrays, so a translation caches in the incremental
// pipeline. The tree is extended by adding records, never by changing these: a new predicate
// form is a new SqlExpression, a COUNT or EXISTS wrapper a new SqlStatement.
//
// The AST names entity properties, not columns. The entity maps are a separate pipeline input,
// so names are resolved when the statement is bound to them and rendered; see LinqQueryBinder.

/// <summary>A whole statement; the renderer switches on the concrete type.</summary>
internal abstract record SqlStatement;

/// <summary>
/// <c>SELECT projection FROM table [WHERE predicates] [ORDER BY ordering] [LIMIT] [OFFSET]</c>.
/// </summary>
/// <param name="From">The queried entity.</param>
/// <param name="Projection">The SELECT list, in the order the row type's constructor reads it.</param>
/// <param name="Predicates">WHERE conditions, joined with AND. Empty for no WHERE clause.</param>
/// <param name="Ordering">ORDER BY keys, in order. Empty for no ORDER BY clause.</param>
/// <param name="Paging">LIMIT and OFFSET.</param>
internal sealed record SqlSelectStatement(
    SqlTableSource From,
    EquatableArray<SqlExpression> Projection,
    EquatableArray<SqlExpression> Predicates,
    EquatableArray<SqlOrdering> Ordering,
    SqlPaging Paging) : SqlStatement;

/// <summary>The entity a query reads, from <c>Sql.Table&lt;T&gt;()</c>.</summary>
/// <param name="EntityTypeFullName">The entity type in the generator's TypeDisplayFormat, the key of its map.</param>
/// <param name="Location">The <c>Sql.Table&lt;T&gt;()</c> call, where ZAO104 is reported.</param>
internal sealed record SqlTableSource(string EntityTypeFullName, LocationInfo? Location);

/// <summary>A value inside a statement: a column, a parameter, a literal, or a later form.</summary>
internal abstract record SqlExpression;

/// <summary>An entity member, such as <c>t.Title</c>; bound to its mapped column at render time.</summary>
/// <param name="Member">The C# member name.</param>
/// <param name="Location">The member access, where ZAO105 is reported.</param>
internal sealed record SqlMemberExpression(string Member, LocationInfo? Location) : SqlExpression;

/// <summary>A companion parameter, by the SQL name the method binds it with.</summary>
internal sealed record SqlParameterExpression(string Name) : SqlExpression;

/// <summary>A compile-time constant, inlined as a SQL literal.</summary>
/// <param name="Kind">How the renderer writes the value.</param>
/// <param name="Value">
/// Invariant text: the number for <see cref="SqlLiteralKind.Number"/>, <c>true</c> or <c>false</c>
/// for <see cref="SqlLiteralKind.Boolean"/>, the raw unescaped text for
/// <see cref="SqlLiteralKind.String"/>, empty for <see cref="SqlLiteralKind.Null"/>.
/// </param>
internal sealed record SqlLiteralExpression(SqlLiteralKind Kind, string Value) : SqlExpression
{
    public static readonly SqlLiteralExpression Null = new(SqlLiteralKind.Null, string.Empty);

    public static SqlLiteralExpression Number(string value) => new(SqlLiteralKind.Number, value);
}

internal enum SqlLiteralKind
{
    Null,
    Boolean,
    Number,
    String,
}

/// <summary>One ORDER BY key.</summary>
internal sealed record SqlOrdering(SqlExpression Expression, bool Descending);

/// <summary>LIMIT and OFFSET, each null when absent.</summary>
internal sealed record SqlPaging(SqlExpression? Limit, SqlExpression? Offset)
{
    public static readonly SqlPaging None = new(null, null);
}
