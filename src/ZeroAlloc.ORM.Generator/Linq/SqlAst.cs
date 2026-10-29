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
/// <c>SELECT projection FROM table [AS alias] [INNER JOIN ...] [WHERE predicates] [GROUP BY groupBy] [ORDER BY ordering] [LIMIT] [OFFSET] [FOR UPDATE ...]</c>.
/// </summary>
/// <param name="From">The queried entity.</param>
/// <param name="Projection">The SELECT list, in the order the row type's constructor reads it.</param>
/// <param name="Predicates">WHERE conditions, joined with AND. Empty for no WHERE clause.</param>
/// <param name="Ordering">ORDER BY keys, in order. Empty for no ORDER BY clause.</param>
/// <param name="Paging">LIMIT and OFFSET.</param>
/// <param name="GroupBy">
/// GROUP BY keys, in the order the key selector names them. Empty for no GROUP BY clause. A
/// grouped projection and ordering read only these keys and aggregate calls.
/// </param>
/// <param name="Joins">The inner joins, in join order. Empty for a query without a join.</param>
/// <param name="Alias">
/// The alias of <paramref name="From"/>, <c>t0</c>, set exactly when the query has a join. Then
/// every member names its source's alias; without a join neither is aliased, as in 2.5.0.
/// </param>
/// <param name="Lock">
/// The row lock, rendered after LIMIT and OFFSET; <see cref="SqlRowLock.None"/> renders nothing.
/// Only a statement's own SELECT locks: never a subquery, a GROUP BY or an aggregate's source.
/// </param>
internal sealed record SqlSelectStatement(
    SqlTableSource From,
    EquatableArray<SqlExpression> Projection,
    EquatableArray<SqlExpression> Predicates,
    EquatableArray<SqlOrdering> Ordering,
    SqlPaging Paging,
    EquatableArray<SqlExpression> GroupBy = default,
    EquatableArray<SqlJoin> Joins = default,
    string? Alias = null,
    SqlRowLock Lock = SqlRowLock.None) : SqlStatement;

/// <summary>The row lock a SELECT takes: a <c>ForUpdate</c>, <c>ForUpdateSkipLocked</c> or <c>ForUpdateNoWait</c> operator.</summary>
internal enum SqlRowLock
{
    /// <summary>No lock, and no clause.</summary>
    None,

    /// <summary><c>FOR UPDATE</c>: another writer or locker waits until the transaction ends.</summary>
    Update,

    /// <summary><c>FOR UPDATE SKIP LOCKED</c>: rows another transaction holds are passed over.</summary>
    UpdateSkipLocked,

    /// <summary><c>FOR UPDATE NOWAIT</c>: a row another transaction holds fails the statement, SQLSTATE 55P03.</summary>
    UpdateNoWait,
}

/// <summary>
/// <c>INNER JOIN table AS alias ON on [AND sourcePredicates]</c>: a <c>Join</c> operator. The
/// inner source's own <c>Where</c> conditions are scoped to it in the ON clause, which for an
/// inner join keeps the same rows as a WHERE would.
/// </summary>
/// <param name="Source">The joined entity.</param>
/// <param name="Alias">Its alias: <c>t1</c>, <c>t2</c>, ... in join order.</param>
/// <param name="On">The key equality, outer key first.</param>
/// <param name="SourcePredicates">The inner source's Where conditions, joined with AND.</param>
internal sealed record SqlJoin(
    SqlTableSource Source,
    string Alias,
    SqlExpression On,
    EquatableArray<SqlExpression> SourcePredicates);

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

/// <summary>
/// A <c>Sum</c>, <c>Min</c>, <c>Max</c> or <c>Average</c> terminal over a SELECT:
/// <c>SELECT call FROM ... WHERE ...</c>, one value read through the <c>[Query]</c> scalar shapes.
/// A record of its own rather than a <see cref="SqlAggregateStatement"/> with an optional call,
/// so each statement says one thing: that one is a bare count or existence, this one a call.
/// </summary>
/// <param name="Call">The aggregate, over a column of <paramref name="Source"/>.</param>
/// <param name="Source">
/// The rows the aggregate reads: the chain's table, WHERE and paging, without a projection. It
/// has an ordering only when it is paged, since which rows a page holds changes a sum, and an
/// ORDER BY next to an aggregate without a GROUP BY is an error.
/// </param>
internal sealed record SqlScalarAggregateStatement(SqlAggregateCallExpression Call, SqlSelectStatement Source) : SqlStatement;

/// <summary>
/// <c>DELETE FROM table [WHERE predicates]</c>: an <c>ExecuteDelete()</c> terminal of a
/// <c>[LinqCommand]</c> chain, whose rows affected the method returns. Without predicates it
/// deletes every row, as EF Core's <c>ExecuteDelete</c> does; nothing is added.
/// </summary>
/// <param name="Table">The entity whose rows are deleted.</param>
/// <param name="Predicates">WHERE conditions, joined with AND. Empty for no WHERE clause.</param>
/// <remarks>
/// The table takes no alias. A command chain has no join, so every member names the table
/// itself, and an IN subquery is uncorrelated: it binds through a scope of its own and aliases
/// its sources s0, s1, ... only when it has a join, so nothing inside it names the outer table.
/// An alias becomes needed only once a predicate can read the outer row from a subquery.
/// </remarks>
internal sealed record SqlDeleteStatement(SqlTableSource Table, EquatableArray<SqlExpression> Predicates) : SqlStatement;

/// <summary>
/// <c>UPDATE table SET a = x, b = y [WHERE predicates]</c>: an <c>ExecuteUpdate(...)</c> terminal of
/// a <c>[LinqCommand]</c> chain, whose rows affected the method returns. Without predicates it
/// updates every row, as EF Core's <c>ExecuteUpdate</c> does; nothing is added.
/// </summary>
/// <param name="Table">The entity whose rows are updated.</param>
/// <param name="Setters">One SET item per <c>SetProperty</c>, in written order; never empty.</param>
/// <param name="Predicates">WHERE conditions, joined with AND. Empty for no WHERE clause.</param>
/// <remarks>
/// The table takes no alias, for the reasons <see cref="SqlDeleteStatement"/> gives. A setter's
/// value reads the row as it was before the statement, as every SET item does in PostgreSQL.
/// </remarks>
internal sealed record SqlUpdateStatement(
    SqlTableSource Table,
    EquatableArray<SqlSetter> Setters,
    EquatableArray<SqlExpression> Predicates) : SqlStatement;

/// <summary>
/// One <c>SetProperty</c>: <c>"Col" = value</c>. The target names the entity member, as every
/// member in the tree does; the map gives its column when the statement is bound, and decides
/// there what an object member is: an owned object, a composite, or one plain column.
/// </summary>
/// <param name="Column">
/// The member the setter writes, such as <c>State</c> or the owned <c>Outcome.PullRequestUrl</c>.
/// Its location is the target lambda's body, where a target the map rejects is reported.
/// </param>
/// <param name="Value">
/// A column of the row, a companion parameter, a literal, or a <see cref="SqlArithmeticExpression"/>
/// over those.
/// </param>
/// <param name="ColumnJson">
/// The target is a string marked <c>[Json]</c> on its property or on the record constructor
/// parameter that feeds it. The binder also counts a map's <c>Json = true</c>; either makes the
/// column jsonb, see <see cref="JsonSetters"/>.
/// </param>
/// <param name="ValueJson">The same for a column of the row the value copies; false for any other value.</param>
internal sealed record SqlSetter(SqlMemberExpression Column, SqlExpression Value, bool ColumnJson = false, bool ValueJson = false);

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
/// <param name="SourceAlias">
/// The alias of the source the member belongs to, <c>t0</c>, <c>t1</c>, ..., in a query with a
/// join; it renders as <c>t1."Col"</c> and resolves through that source's map. Null without a
/// join, which renders the bare column as in 2.5.0.
/// </param>
/// <param name="Json">
/// Set on a member read in a condition whose string property is marked <c>[Json]</c>, on itself or
/// on the record constructor parameter that feeds it. A column the map declares <c>Json = true</c>
/// is not flagged here; the name scope knows it.
/// </param>
/// <param name="WholeObject">
/// Set on a projected member whose type is an object: the map decides when the query is bound
/// whether it is a plain column, an owned object, ZAO107, or a composite, ZAO102. Null for any
/// other member.
/// </param>
internal sealed record SqlMemberExpression(
    string Member,
    LocationInfo? Location,
    bool ColumnPicksEnumStorage,
    string? SourceAlias = null,
    SqlWholeObject? WholeObject = null,
    bool Json = false) : SqlExpression;

/// <summary>
/// A PostgreSQL system column of a row, named by a marker rather than by the map: <c>Sql.Xmin(r)</c>
/// is <c>"xmin"</c>, or <c>t0."xmin"</c> in a query with a join. xmin is a transaction id, read as a
/// <c>uint</c> and compared, by <c>==</c> or <c>!=</c> only, with a <c>uint</c> parameter, which
/// binds as <c>xid</c>; the reader rejects it anywhere else. A property the map maps to xmin is a
/// <see cref="SqlMemberExpression"/> whose column the render recognises; see SqlNameScope.
/// </summary>
/// <param name="Name">The column, <c>xmin</c>.</param>
/// <param name="Alias">The alias of the row's source in a query with a join; null without one.</param>
internal sealed record SqlSystemColumnExpression(string Name, string? Alias) : SqlExpression;

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
/// <c>(left op right)</c>: C#'s <c>+</c>, <c>-</c> or <c>*</c> on <c>int</c>, <c>long</c>,
/// <c>decimal</c>, <c>double</c> or <c>float</c>, read in a setter's value only. Always
/// parenthesised, so the tree's grouping survives the flat text. A NULL operand makes it NULL,
/// as C#'s lifted operators do. An integer result out of its type's range raises PostgreSQL's
/// error, where unchecked C# would wrap: a known difference.
/// </summary>
internal sealed record SqlArithmeticExpression(SqlArithmeticOperator Operator, SqlExpression Left, SqlExpression Right) : SqlExpression;

internal enum SqlArithmeticOperator
{
    Add,
    Subtract,
    Multiply,
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
/// A null test on a row member whose type is an object, such as <c>r.Outcome == null</c> or
/// <c>r.Website != null</c> for a <c>Uri?</c>. The map decides what it is when the query is bound:
/// for an owned object every column the map gives its members is NULL,
/// <c>("Outcome_A" IS NULL AND "Outcome_B" IS NULL)</c>, or its NOT when <paramref name="Negated"/>;
/// for a plain column it is <c>"Col" IS [NOT] NULL</c>; a composite column is ZAO102, as before;
/// anything else ZAO105.
/// </summary>
/// <param name="Member">The entity property, such as <c>Outcome</c>.</param>
/// <param name="Text">The tested expression as written, for messages.</param>
/// <param name="CompositeType">
/// The member's type when it maps to several columns as a composite, for the ZAO102 a plain
/// composite column gets; null for any other type.
/// </param>
/// <param name="Location">The tested member, where a diagnostic points.</param>
/// <param name="Negated">Whether the test is <c>!= null</c>.</param>
/// <param name="SourceAlias">The alias of the member's source in a query with a join, as on a member.</param>
internal sealed record SqlObjectIsNullExpression(
    string Member,
    string Text,
    string? CompositeType,
    LocationInfo? Location,
    bool Negated,
    string? SourceAlias = null) : SqlExpression;

/// <summary>
/// A projected row member whose type is an object, such as <c>r.Outcome</c>; see
/// <see cref="SqlMemberExpression.WholeObject"/>. Not an expression of its own.
/// </summary>
/// <param name="Text">The member as written, for messages.</param>
/// <param name="Type">The member's type as messages show it.</param>
/// <param name="CompositeType">
/// The type when it maps to several columns as a composite; null for any other type.
/// </param>
internal sealed record SqlWholeObject(string Text, string Type, string? CompositeType);

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
/// <c>query.Select(p =&gt; p.Col).Contains(operand)</c>: <c>operand IN (SELECT "Col" FROM ... WHERE
/// ... ORDER BY ... LIMIT ... OFFSET ...)</c>, or <c>NOT IN</c> when <paramref name="Negated"/>. The
/// ordering and the paging stay inside the subquery, so a paged parent set selects exactly the
/// parents' ids. The selected column is non-nullable, which the reader checks, so NOT IN has no
/// NULL trap. The operand may be null: the test is then NULL, or false for an empty subquery,
/// which a WHERE drops alike, and C#'s <c>!</c> over it takes IS NOT TRUE rather than NOT IN.
/// </summary>
/// <param name="Operand">The outer value, a column or parameter of the outer query.</param>
/// <param name="Subquery">
/// The uncorrelated subquery, projecting exactly one column. Its sources are aliased <c>s0</c>,
/// <c>s1</c>, ... only when it has a join, and it binds through its own name scope.
/// </param>
/// <param name="Negated">Whether the test is NOT IN, C#'s <c>!</c> over the Contains of a non-nullable operand.</param>
internal sealed record SqlInSubqueryExpression(SqlExpression Operand, SqlSelectStatement Subquery, bool Negated) : SqlExpression;

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

/// <summary>
/// An aggregate call: <c>COALESCE(fn(argument), 0)::cast</c>, each part optional. Scalar
/// aggregates use it as their whole SELECT list; a grouped SELECT can project several.
/// </summary>
/// <param name="Function">The aggregate function.</param>
/// <param name="Argument">The aggregated column; null for <c>count(*)</c>.</param>
/// <param name="ResultCast">
/// The PostgreSQL type the result is cast to, so it is exactly the CLR type the scalar reads,
/// such as <c>int</c> for a <c>Sum</c> over int, whose <c>sum</c> is bigint. Null for none.
/// </param>
/// <param name="CoalesceToZero">
/// Whether NULL, the aggregate of no values, reads as 0. LINQ's <c>Sum</c> is 0 for no values,
/// nullable selectors included.
/// </param>
internal sealed record SqlAggregateCallExpression(
    SqlAggregateFunction Function,
    SqlExpression? Argument,
    string? ResultCast,
    bool CoalesceToZero) : SqlExpression;

/// <summary>
/// C#'s <c>.Date</c> on a <c>DateTime</c> or <c>DateTimeOffset</c> column, as EF Core's Npgsql
/// provider translates it.
/// <list type="bullet">
/// <item>A <c>DateTimeOffset</c> column is a <c>timestamptz</c>, which Npgsql reads at offset 0, so
/// C#'s <c>.Date</c> is the UTC date: <c>date_trunc('day', col AT TIME ZONE 'UTC')</c>, a
/// <c>timestamp</c> read with Kind Unspecified, as C#'s is. It does not depend on the session's
/// <c>TimeZone</c>.</item>
/// <item>A <c>DateTime</c> column may be a <c>timestamp</c> or a <c>timestamptz</c>, which only
/// its map tells apart, so the render decides from the column's
/// <see cref="ColumnMapModel.DateTimeStorage"/>. A <c>timestamptz</c> Npgsql reads as a UTC
/// <c>DateTime</c>, so C#'s <c>.Date</c> is the UTC day with Kind Utc:
/// <c>date_trunc('day', col, 'UTC')</c>, a <c>timestamptz</c> read with Kind Utc, whatever the
/// session's <c>TimeZone</c>. A <c>timestamp</c>, or a column whose map declares nothing:
/// <c>date_trunc('day', col)</c>. Over an undeclared <c>timestamptz</c> its day boundary follows
/// the session's <c>TimeZone</c>.</item>
/// </list>
/// </summary>
/// <param name="Operand">The column.</param>
/// <param name="AtUtc">
/// Whether the column is a <c>DateTimeOffset</c>, a <c>timestamptz</c> cut at the UTC day as a
/// <c>timestamp</c>. False for a <c>DateTime</c> column, whatever its storage.
/// </param>
internal sealed record SqlDateTruncExpression(SqlExpression Operand, bool AtUtc) : SqlExpression;

internal enum SqlAggregateFunction
{
    /// <summary><c>count</c>.</summary>
    Count,

    /// <summary><c>count</c>, read as a long.</summary>
    LongCount,

    /// <summary><c>sum</c>.</summary>
    Sum,

    /// <summary><c>min</c>.</summary>
    Min,

    /// <summary><c>max</c>.</summary>
    Max,

    /// <summary><c>avg</c>.</summary>
    Average,
}

/// <summary>
/// One ORDER BY key. <paramref name="NullsLikeLinq"/> is set when the key can be NULL: LINQ sorts
/// null first ascending and last descending, where PostgreSQL's default is the opposite, so the
/// renderer says NULLS FIRST or NULLS LAST. A key that cannot be NULL renders without it.
/// </summary>
internal sealed record SqlOrdering(SqlExpression Expression, bool Descending, bool NullsLikeLinq = false);

/// <summary>LIMIT and OFFSET, each null when absent.</summary>
internal sealed record SqlPaging(SqlExpression? Limit, SqlExpression? Offset)
{
    public static readonly SqlPaging None = new(null, null);
}
