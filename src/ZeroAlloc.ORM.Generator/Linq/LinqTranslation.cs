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
}

/// <summary>
/// What <see cref="LinqQueryReader.Read"/> makes of one <c>[LinqQuery]</c> method: the statement,
/// the result shape, the parameter facets and the source comment, or the diagnostics that stop
/// the translation. Symbol-free, so the per-method read caches.
/// </summary>
/// <param name="Statement">The translated statement; null exactly when <paramref name="Diagnostics"/> holds an error.</param>
/// <param name="Shape">The result shape the method's return type selects.</param>
/// <param name="Facets">The DbType of each value parameter, applied as <c>[Param(DbType)]</c> would be.</param>
/// <param name="SourceComment">The comment above the SQL constant: <c>LINQ: Type.Companion</c>.</param>
/// <param name="Diagnostics">Front-end diagnostics, reported at their nodes.</param>
internal sealed record LinqTranslation(
    SqlStatement? Statement,
    LinqResultShape Shape,
    EquatableArray<LinqParameterFacet> Facets,
    string SourceComment,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    public static LinqTranslation Failed(string sourceComment, EquatableArray<DiagnosticInfo> diagnostics)
        => new(null, LinqResultShape.List, default, sourceComment, diagnostics);
}

/// <summary>
/// A translation bound to the entity maps: the rendered SQL, and what the materializer must read
/// differently from the row type's own conventions. Produced by <see cref="LinqQueryBinder.Bind"/>.
/// </summary>
/// <param name="Sql">The rendered SQL; empty when <paramref name="Diagnostics"/> holds an error.</param>
/// <param name="EnumAsStringOrdinals">
/// SELECT positions that read an <c>EnumAsString</c> column, so an enum member there parses the
/// member name instead of casting an integer.
/// </param>
/// <param name="Diagnostics">Binding diagnostics: ZAO104 and ZAO105.</param>
internal sealed record LinqBoundQuery(
    string Sql,
    EquatableArray<int> EnumAsStringOrdinals,
    EquatableArray<DiagnosticInfo> Diagnostics);

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
