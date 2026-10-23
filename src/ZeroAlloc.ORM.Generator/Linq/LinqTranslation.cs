using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>How the partial method returns the companion's rows.</summary>
internal enum LinqResultShape
{
    /// <summary><c>Task</c> or <c>ValueTask</c> of <c>List|IReadOnlyList|IList&lt;TRow&gt;</c>.</summary>
    List,

    /// <summary><c>Task&lt;TRow?&gt;</c> or <c>ValueTask&lt;TRow?&gt;</c>: the first row, with an implicit <c>LIMIT 1</c>.</summary>
    SingleRow,

    /// <summary><c>IAsyncEnumerable&lt;TRow&gt;</c>.</summary>
    Stream,

    /// <summary>
    /// <c>Task</c> or <c>ValueTask</c> of <c>int</c>, <c>long</c> or <c>bool</c>: the value a
    /// <c>Count()</c>, <c>LongCount()</c> or <c>Any()</c> terminal produces.
    /// </summary>
    Scalar,

    /// <summary>
    /// <c>Task</c> or <c>ValueTask</c> of a tuple, optionally nullable, for a companion returning a
    /// tuple of <c>IQuery&lt;T&gt;</c>: one statement and result set per element, each element
    /// a list, a single row or a scalar.
    /// </summary>
    MultiResult,

    /// <summary>
    /// <c>Task</c> or <c>ValueTask</c>, or <c>Task&lt;int&gt;</c> or <c>ValueTask&lt;int&gt;</c> for the
    /// rows affected: a <c>[LinqCommand]</c> write, run through the <c>[Command]</c> non-query path.
    /// </summary>
    NonQuery,
}

/// <summary>
/// What <see cref="LinqQueryReader.Read"/> makes of one <c>[LinqQuery]</c> or <c>[LinqCommand]</c> method: the statements,
/// the result shape, the parameter facets and the source comment, or the diagnostics that stop
/// the translation. Symbol-free, so the per-method read caches.
/// </summary>
/// <param name="Statements">
/// The translated statements, in order: one, or one per tuple element of a tuple companion.
/// Empty exactly when <paramref name="Diagnostics"/> holds an error.
/// </param>
/// <param name="Shape">The result shape the method's return type selects; <see cref="LinqResultShape.MultiResult"/> for a tuple.</param>
/// <param name="Facets">The DbType of each value parameter, applied as <c>[Param(DbType)]</c> would be.</param>
/// <param name="SourceComment">The comment above the SQL constant: <c>LINQ: Type.Companion</c>.</param>
/// <param name="Diagnostics">Front-end diagnostics, reported at their nodes.</param>
internal sealed record LinqTranslation(
    EquatableArray<SqlStatement> Statements,
    LinqResultShape Shape,
    EquatableArray<LinqParameterFacet> Facets,
    string SourceComment,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    public static LinqTranslation Failed(string sourceComment, EquatableArray<DiagnosticInfo> diagnostics)
        => new(default, LinqResultShape.List, default, sourceComment, diagnostics);

    public bool Succeeded => Statements.Length > 0;

    /// <summary>
    /// Projected members of an object that may be an optional owned one, in constructor
    /// parameters that cannot take a null; the binder reports ZAO107 for those the map owns.
    /// </summary>
    public EquatableArray<LinqLiftedProjection> LiftedProjections { get; init; }

    /// <summary>
    /// The C# names of the <c>uint</c> value parameters compared with xmin, through
    /// <c>Sql.Xmin</c> or a <c>uint</c> property the binder checks maps to xmin, sorted by
    /// name: they bind as <c>xid</c>.
    /// </summary>
    public EquatableArray<string> XidParameters { get; init; }

    /// <summary>
    /// Projected <c>uint</c> members, read as xmin: the binder reports ZAO107 for each whose
    /// column is not xmin.
    /// </summary>
    public EquatableArray<LinqXminProjection> XminProjections { get; init; }
}

/// <summary>
/// A projected <c>uint</c> member, such as <c>t.RowVersion</c>, which the row reads with
/// <c>GetFieldValue&lt;uint&gt;</c>: only xmin reads as one, so its map must map it to xmin.
/// </summary>
/// <param name="Statement">The statement the projection belongs to.</param>
/// <param name="Member">The projected member.</param>
/// <param name="Location">The projected value.</param>
internal sealed record LinqXminProjection(int Statement, SqlMemberExpression Member, LocationInfo? Location);

/// <summary>
/// A projected member of an optional object, such as <c>r.Outcome!.HasFailedValidation</c>, whose
/// constructor parameter cannot take the NULL it reads when the object is absent. ZAO107 when
/// the map declares <paramref name="Owner"/> owned; otherwise the path is unmapped, ZAO105.
/// </summary>
/// <param name="Statement">The statement the projection belongs to.</param>
/// <param name="Owner">The entity property the member is reached through, such as <c>Outcome</c>.</param>
/// <param name="SourceAlias">The owner's source alias in a query with a join.</param>
/// <param name="Reason">The ZAO107 message.</param>
/// <param name="Location">The projected value.</param>
internal sealed record LinqLiftedProjection(
    int Statement,
    string Owner,
    string? SourceAlias,
    string Reason,
    LocationInfo? Location);

/// <summary>
/// A translation bound to the entity maps: the rendered SQL, and what the materializer must read
/// differently from the row type's own conventions. Produced by <see cref="LinqQueryBinder.Bind(EquatableArray{SqlStatement}, EquatableArray{EntityMapModel})"/>.
/// </summary>
/// <param name="Sql">
/// The rendered SQL, its statements joined with <c>;\n</c>; empty when
/// <paramref name="Diagnostics"/> holds an error.
/// </param>
/// <param name="EnumAsStringOrdinals">
/// Per statement, the SELECT positions that read an <c>EnumAsString</c> column, so an enum member
/// there parses the member name instead of casting an integer.
/// </param>
/// <param name="NameBoundEnumParameters">
/// SQL names of the enum parameters without <c>[StoreAsString]</c> that are compared with
/// <c>EnumAsString</c> columns, so they bind as their member name.
/// </param>
/// <param name="Diagnostics">Binding diagnostics: ZAO102, ZAO104, ZAO105 and ZAO111.</param>
internal sealed record LinqBoundQuery(
    string Sql,
    EquatableArray<EquatableArray<int>> EnumAsStringOrdinals,
    EquatableArray<string> NameBoundEnumParameters,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    /// <summary>Each statement's rendered SQL, in order; <see cref="Sql"/> is these joined.</summary>
    public EquatableArray<string> Statements { get; init; }

    /// <summary>
    /// The parameters an UPDATE sets into a jsonb column, by map or by <c>[Json]</c>, each at its
    /// first such setter: they bind as jsonb, as a <c>[Json]</c> parameter does.
    /// </summary>
    public EquatableArray<SqlParameterExpression> JsonParameters { get; init; }
}

/// <summary>
/// The per-method read: the translation, and the <c>[Query]</c>-path model built from the method
/// when the translation succeeded. Its SQL is filled in when the read is bound to the maps.
/// </summary>
internal sealed record LinqMethodRead(LinqTranslation Translation, QueryMethodWithTypeContext? Core);

/// <summary>
/// A bound <c>[LinqQuery]</c> method: the model that joins the repository, null when a diagnostic
/// stops it, and the LINQ diagnostics to report on their own.
/// </summary>
internal sealed record LinqMethodResult(QueryMethodWithTypeContext? Method, EquatableArray<DiagnosticInfo> Diagnostics);
