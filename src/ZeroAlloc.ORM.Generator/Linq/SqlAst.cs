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

/// <summary>
/// A <c>Count()</c>, <c>LongCount()</c> or <c>Any()</c> terminal over a SELECT: one value, read
/// through the <c>[Query]</c> scalar shapes.
/// </summary>
/// <param name="Aggregate">Which terminal.</param>
/// <param name="Source">
/// The rows the terminal reads: the chain's table, WHERE and paging. It has no projection and
/// no ordering, since neither changes a count or an existence.
/// </param>
internal sealed record SqlAggregateStatement(SqlAggregate Aggregate, SqlSelectStatement Source) : SqlStatement;

internal enum SqlAggregate
{
    /// <summary><c>Count()</c>: <c>count(*)::int</c>.</summary>
    Count,

    /// <summary><c>LongCount()</c>: <c>count(*)</c>, a bigint.</summary>
    LongCount,

    /// <summary><c>Any()</c>: <c>EXISTS (SELECT 1 ...)</c>.</summary>
    Any,
}

/// <summary>The entity a query reads, from <c>Sql.Table&lt;T&gt;()</c>.</summary>
/// <param name="EntityTypeFullName">The entity type in the generator's TypeDisplayFormat, the key of its map.</param>
/// <param name="Location">The <c>Sql.Table&lt;T&gt;()</c> call, where ZAO104 is reported.</param>
internal sealed record SqlTableSource(string EntityTypeFullName, LocationInfo? Location);

/// <summary>A value inside a statement: a column, a parameter, a literal, or a later form.</summary>
internal abstract record SqlExpression;

/// <summary>An entity member, such as <c>t.Title</c>; bound to its mapped column at render time.</summary>
/// <param name="Member">The C# member name.</param>
/// <param name="Location">The member access, where ZAO105 is reported.</param>
/// <param name="ColumnPicksEnumStorage">
/// The member is an enum without <c>[StoreAsString]</c>, so its map decides whether the column
/// holds the integer or the member name.
/// </param>
internal sealed record SqlMemberExpression(string Member, LocationInfo? Location, bool ColumnPicksEnumStorage) : SqlExpression;

/// <summary>A companion parameter, by the SQL name the method binds it with.</summary>
/// <param name="Name">The SQL name, without the <c>@</c>.</param>
/// <param name="ClrName">The C# parameter name, as diagnostics show it.</param>
/// <param name="ColumnPicksEnumStorage">
/// The parameter is an enum without <c>[StoreAsString]</c>, so the columns it is compared with
/// decide whether it binds as its integer or as its member name; see ZAO111.
/// </param>
/// <param name="Location">The parameter reference, where ZAO111 is reported.</param>
internal sealed record SqlParameterExpression(string Name, string ClrName, bool ColumnPicksEnumStorage, LocationInfo? Location) : SqlExpression;

/// <summary>A compile-time constant, inlined as a SQL literal.</summary>
/// <param name="Kind">How the renderer writes the value.</param>
/// <param name="Value">
/// Invariant text: the number for <see cref="SqlLiteralKind.Number"/> and <see cref="SqlLiteralKind.Real"/>, <c>true</c> or <c>false</c>
/// for <see cref="SqlLiteralKind.Boolean"/>, the raw unescaped text for
/// <see cref="SqlLiteralKind.String"/>, empty for <see cref="SqlLiteralKind.Null"/>.
/// </param>
internal sealed record SqlLiteralExpression(SqlLiteralKind Kind, string Value) : SqlExpression
{
    public static readonly SqlLiteralExpression Null = new(SqlLiteralKind.Null, string.Empty);

    public static SqlLiteralExpression Number(string value) => new(SqlLiteralKind.Number, value);
}

/// <summary>
/// An enum member constant. It renders as its integer, or as its quoted member name where the
/// value is stored by name: when the enum type is <c>[StoreAsString]</c>, or when it is compared
/// with a column the map marks <c>EnumAsString</c>.
/// </summary>
/// <param name="Number">The member's integer value, as invariant text.</param>
/// <param name="Name">The member name; null when the value names no member.</param>
/// <param name="StoresName">The enum type is <c>[StoreAsString]</c>.</param>
/// <param name="Location">The constant, where a nameless value compared by name is reported.</param>
internal sealed record SqlEnumLiteralExpression(string Number, string? Name, bool StoresName, LocationInfo? Location) : SqlExpression;

/// <summary>A comparison, or AND / OR of two conditions.</summary>
internal sealed record SqlBinaryExpression(SqlBinaryOperator Operator, SqlExpression Left, SqlExpression Right) : SqlExpression;

internal enum SqlBinaryOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    And,
    Or,
}

/// <summary>
/// <c>Sql.Row(a, b) &gt; Sql.Row(x, y)</c>: a PostgreSQL row comparison, <c>(a, b) &gt; (x, y)</c>,
/// for keyset paging. Both rows have the same arity, and each pair is a column and a value.
/// </summary>
/// <param name="Operator">One of the four orderings.</param>
/// <param name="Left">The left row's items, in order.</param>
/// <param name="Right">The right row's items, in order.</param>
internal sealed record SqlRowComparisonExpression(
    SqlBinaryOperator Operator,
    EquatableArray<SqlExpression> Left,
    EquatableArray<SqlExpression> Right) : SqlExpression;

/// <summary><c>NOT (operand)</c>, for an operand that is never NULL.</summary>
internal sealed record SqlNotExpression(SqlExpression Operand) : SqlExpression;

/// <summary>
/// <c>(operand) IS NOT TRUE</c>: C#'s <c>!</c> over a condition that can be NULL in SQL, such as a
/// comparison with a nullable column. C# reads <c>null &gt; 0</c> as false, so its negation is
/// true; <c>NOT (NULL)</c> would be NULL and drop the row, where IS NOT TRUE is true.
/// </summary>
internal sealed record SqlIsNotTrueExpression(SqlExpression Operand) : SqlExpression;

/// <summary><c>operand IS NULL</c>, or <c>IS NOT NULL</c> when <paramref name="Negated"/>.</summary>
/// <param name="Operand">The tested value.</param>
/// <param name="Negated">Whether the test is IS NOT NULL.</param>
/// <param name="Cast">
/// The PostgreSQL type a parameter without a DbType facet is cast to here, such as <c>uuid[]</c>,
/// so a null still has a type. PostgreSQL types a parameter at its first use, and a bare
/// <c>@p IS NULL</c> gives it none. Null when the operand is typed already.
/// </param>
internal sealed record SqlIsNullExpression(SqlExpression Operand, bool Negated, string? Cast) : SqlExpression;

/// <summary>
/// <c>operand::type</c>: a parameter without a DbType facet in the SELECT list, so a null still has
/// a type. PostgreSQL reads the SELECT list first and types a bare null parameter there as text,
/// which clashes with a later use such as a comparison with a <c>timestamptz</c> column.
/// </summary>
/// <param name="Operand">The parameter.</param>
/// <param name="Type">The PostgreSQL type, such as <c>timestamptz</c>.</param>
internal sealed record SqlCastExpression(SqlExpression Operand, string Type) : SqlExpression;

/// <summary>
/// <c>string.Contains</c>, <c>StartsWith</c> or <c>EndsWith</c>: a <c>LIKE</c> whose pattern is the
/// value with its wildcards escaped, and <c>%</c> on the open sides.
/// </summary>
internal sealed record SqlLikeExpression(SqlExpression Operand, SqlExpression Value, SqlLikeMatch Match) : SqlExpression;

internal enum SqlLikeMatch
{
    Contains,
    StartsWith,
    EndsWith,
}

/// <summary><c>Sql.ILike(operand, pattern)</c>: <c>operand ILIKE pattern</c>, the pattern as written.</summary>
internal sealed record SqlILikeExpression(SqlExpression Operand, SqlExpression Pattern) : SqlExpression;

/// <summary><c>array.Contains(operand)</c>: <c>operand = ANY(array)</c>, for an array parameter.</summary>
internal sealed record SqlAnyExpression(SqlExpression Operand, SqlExpression Array) : SqlExpression;

/// <summary>
/// <c>WhereIf(condition, predicate)</c>: <c>((condition) IS NOT TRUE OR (predicate))</c>. A NULL
/// condition counts as false, as in C#, so it skips the filter.
/// </summary>
internal sealed record SqlConditionalExpression(SqlExpression Condition, SqlExpression Predicate) : SqlExpression;

internal enum SqlLiteralKind
{
    Null,
    Boolean,
    Number,
    String,

    /// <summary>
    /// A <c>float</c>, written <c>0.1::real</c>. A bare <c>0.1</c> is numeric, which PostgreSQL
    /// compares with a real column as double precision, where the float's value differs.
    /// </summary>
    Real,
}

/// <summary>
/// A Skip or Take parameter, clamped as LINQ reads it: a negative count skips or takes nothing,
/// where PostgreSQL rejects a negative LIMIT or OFFSET. <c>GREATEST(@n, 0)</c>, or
/// <c>LEAST(GREATEST(@n, 0), 1)</c> when the single-row shape reads one row at most.
/// </summary>
/// <param name="Count">The parameter.</param>
/// <param name="AtMostOne">Whether the count is also capped at 1.</param>
internal sealed record SqlPagingCountExpression(SqlExpression Count, bool AtMostOne) : SqlExpression;

/// <summary>One ORDER BY key.</summary>
internal sealed record SqlOrdering(SqlExpression Expression, bool Descending);

/// <summary>LIMIT and OFFSET, each null when absent.</summary>
internal sealed record SqlPaging(SqlExpression? Limit, SqlExpression? Offset)
{
    public static readonly SqlPaging None = new(null, null);
}
