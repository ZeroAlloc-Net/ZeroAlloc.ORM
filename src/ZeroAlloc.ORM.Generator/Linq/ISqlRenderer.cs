using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// The dialect seam: renders a dialect-neutral <see cref="SqlStatement"/> as SQL text. Only
/// <see cref="PostgresSqlRenderer"/> exists today; another dialect adds an implementation.
/// </summary>
internal interface ISqlRenderer
{
    /// <summary>Renders <paramref name="statement"/>, resolving table and column names through <paramref name="names"/>.</summary>
    string Render(SqlStatement statement, SqlNameScope names);
}

/// <summary>
/// Resolves the entity members a statement names to the mapped tables and columns. A member that
/// is not a mapped column is recorded as ZAO105, so one render both produces the SQL and checks
/// every name in it. A query with a join has one source per alias, each with its own map; a
/// member resolves through the source its alias names, or through the FROM source when it has no
/// alias. An IN subquery binds through a child scope of its own, see <see cref="Subquery"/>,
/// whose diagnostics and name-bound parameters are the statement's. Used for one render, never
/// cached.
/// </summary>
internal sealed class SqlNameScope
{
    private readonly SqlNameScope? _parent;
    private readonly EquatableArray<EntityMapModel> _maps;
    private readonly Source _from;
    private readonly Dictionary<string, Source> _aliased = new(StringComparer.Ordinal);
    private readonly Dictionary<SqlSelectStatement, SqlNameScope> _subqueries = new();
    private readonly List<DiagnosticInfo> _diagnostics;
    private readonly HashSet<DiagnosticInfo> _unmapped;
    private readonly HashSet<SqlExpression> _projected = new();
    private ImmutableHashSet<string> _nameBoundParameters = ImmutableHashSet<string>.Empty;

    /// <param name="map">The FROM entity's map.</param>
    /// <param name="entityDisplay">The FROM entity as messages show it.</param>
    /// <param name="alias">The FROM entity's alias, when the statement has a join; null without one.</param>
    /// <param name="maps">Every entity map, which an IN subquery's sources resolve through.</param>
    public SqlNameScope(EntityMapModel map, string entityDisplay, string? alias, EquatableArray<EntityMapModel> maps)
    {
        _maps = maps;
        _diagnostics = new List<DiagnosticInfo>();
        _unmapped = new HashSet<DiagnosticInfo>();
        _from = new Source(map, entityDisplay);
        if (alias is not null) _aliased[alias] = _from;
    }

    // A subquery's scope: its own sources, the statement's diagnostics.
    private SqlNameScope(SqlNameScope parent, EntityMapModel map, string entityDisplay, string? alias)
    {
        _parent = parent;
        _maps = parent._maps;
        _diagnostics = parent._diagnostics;
        _unmapped = parent._unmapped;
        _from = new Source(map, entityDisplay);
        if (alias is not null) _aliased[alias] = _from;
    }

    public string Table => _from.Map.Table;

    public string? Schema => _from.Map.Schema;

    public ImmutableArray<DiagnosticInfo> Diagnostics => _diagnostics.ToImmutableArray();

    /// <summary>
    /// Whether a member renders with its source's alias. A paged aggregate turns it off for the
    /// call outside its subquery, which reads the subquery's one column.
    /// </summary>
    public bool QualifyColumns { get; set; } = true;

    /// <summary>Adds a joined source under its alias.</summary>
    public void AddSource(string alias, EntityMapModel map, string entityDisplay)
        => _aliased[alias] = new Source(map, entityDisplay);

    /// <summary>The table and schema of the source <paramref name="alias"/> names.</summary>
    public (string Table, string? Schema) TableOf(string alias)
    {
        var map = SourceOf(alias).Map;
        return (map.Table, map.Schema);
    }

    /// <summary>
    /// The member's column, or the member name after recording ZAO105. One member renders more
    /// than once, such as a group key in SELECT, GROUP BY and ORDER BY, so ZAO105 is recorded
    /// once per member and location.
    /// </summary>
    public string Column(SqlMemberExpression member)
    {
        var source = SourceOf(member.SourceAlias);
        if (member.WholeObject is { } whole)
        {
            return ResolveObject(source, member.Member, whole.Text, whole.CompositeType, member.Location, projected: true, out _)
                ?? member.Member;
        }

        if (source.Columns.TryGetValue(member.Member, out var column)) return column.Column;
        Unmapped(source, member.Member, member.Location, _projected.Contains(member));
        return member.Member;
    }

    /// <summary>
    /// The statement's SELECT list, so a member that is not mapped there reports in a
    /// projection's terms, ZAO107, and elsewhere ZAO102 or ZAO105.
    /// </summary>
    public void Projected(EquatableArray<SqlExpression> projection)
    {
        foreach (var item in projection) _projected.Add(item);
    }

    /// <summary>Whether the map of the source <paramref name="alias"/> names declares the property owned.</summary>
    public bool IsOwned(string owner, string? alias) => IsOwned(SourceOf(alias), owner);

    // A path the map has no column for. Through a type nested in an owned object, such as
    // Repository.Origin.FilePath, it says so: [OwnedMap] maps one level, ZAO107 in a projection
    // and ZAO102 elsewhere. Any other path is ZAO105, a navigation such as Task.Title included.
    private void Unmapped(Source source, string path, LocationInfo? location, bool projected)
    {
        var segments = path.Split('.');
        if (segments.Length >= 3 && IsOwned(source, segments[0]))
        {
            var reason = "'" + path + "' reads '" + segments[1] + "', a type nested in the owned '" + segments[0]
                + "'; [OwnedMap] maps one level";
            Record(projected
                ? LinqDiagnostics.UnsupportedProjectionAt(reason, location)
                : LinqDiagnostics.UnsupportedAt(reason, location));
            return;
        }

        Record(LinqDiagnostics.UnmappedMember(path, source.EntityDisplay, location));
    }

    private static bool IsOwned(Source source, string owner)
    {
        foreach (var column in source.Map.Columns)
        {
            if (string.Equals(column.Owner, owner, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// What a null test on an object member is, by the map: the owned object's columns, in map
    /// order, or the one plain column it is. Empty with no plain column after recording the
    /// diagnostic, once per member and location.
    /// </summary>
    public ImmutableArray<string> ObjectColumns(SqlObjectIsNullExpression test, out string? plain)
    {
        plain = ResolveObject(SourceOf(test.SourceAlias), test.Member, test.Text, test.CompositeType, test.Location, projected: false, out var owned);
        return owned;
    }

    // The one decision for a row member whose type is an object, read whole. The map decides:
    // an owned object gives its columns, and reading it in a projection is ZAO107; a composite
    // column is ZAO102, as it was before owned types; a plain column is that column; anything
    // else ZAO105. Returns the plain column, or null.
    private string? ResolveObject(
        Source source,
        string member,
        string text,
        string? compositeType,
        LocationInfo? location,
        bool projected,
        out ImmutableArray<string> owned)
    {
        var columns = ImmutableArray.CreateBuilder<string>();
        foreach (var column in source.Map.Columns)
        {
            if (string.Equals(column.Owner, member, StringComparison.Ordinal))
                columns.Add(column.Column);
        }
        owned = columns.ToImmutable();

        if (owned.Length > 0)
        {
            if (projected)
            {
                Record(LinqDiagnostics.UnsupportedProjectionAt(
                    "'" + text + "' reads a whole owned '" + member + "', an object rather than one column; "
                        + "project the columns it holds",
                    location));
            }
            return null;
        }

        if (compositeType is not null)
        {
            Record(LinqDiagnostics.UnsupportedAt(
                "'" + text + "', whose type '" + compositeType + "' maps to several columns,", location));
            return null;
        }

        if (source.Columns.TryGetValue(member, out var plain)) return plain.Column;

        Unmapped(source, member, location, projected);
        return null;
    }

    // A diagnostic the render found, once per diagnostic: one member renders more than once.
    private void Record(DiagnosticInfo diagnostic)
    {
        if (_unmapped.Add(diagnostic)) _diagnostics.Add(diagnostic);
    }

    /// <summary>
    /// SQL names of the enum parameters that bind as their member name, decided by
    /// <see cref="EnumParameterStorage"/> before the render.
    /// </summary>
    public ImmutableHashSet<string> NameBoundParameters
    {
        get => _parent?.NameBoundParameters ?? _nameBoundParameters;
        set => _nameBoundParameters = value;
    }

    /// <summary>
    /// The scope an IN subquery binds through: its FROM source, under its alias when it has a
    /// join, and each joined source under its own. One scope per subquery, so a subquery walked
    /// twice resolves alike.
    /// </summary>
    public SqlNameScope Subquery(SqlSelectStatement select)
    {
        if (_subqueries.TryGetValue(select, out var scope)) return scope;
        scope = new SqlNameScope(this, MapOf(select.From), LinqQueryBinder.DisplayName(select.From.EntityTypeFullName), select.Alias);
        foreach (var join in select.Joins)
            scope.AddSource(join.Alias, MapOf(join.Source), LinqQueryBinder.DisplayName(join.Source.EntityTypeFullName));
        _subqueries[select] = scope;
        return scope;
    }

    // The map of a subquery's source. The binder has checked every subquery source against the
    // maps before the render, reporting ZAO104 for one without a map, so each has one here.
    private EntityMapModel MapOf(SqlTableSource source)
        => LinqQueryBinder.FindMap(_maps, source.EntityTypeFullName)
            ?? throw new InvalidOperationException("No map for subquery source " + source.EntityTypeFullName + ".");

    /// <summary>Records a diagnostic the render found, such as an enum value with no name to compare by.</summary>
    public void Report(DiagnosticInfo diagnostic) => _diagnostics.Add(diagnostic);

    /// <summary>Whether the member maps to a column that stores an enum as its member name.</summary>
    public bool IsEnumAsString(SqlMemberExpression member)
        => SourceOf(member.SourceAlias).Columns.TryGetValue(member.Member, out var column) && column.EnumAsString;

    /// <summary>
    /// Whether the member maps to a <c>DateTime</c> column its map declares a <c>timestamptz</c>,
    /// through the source its alias names.
    /// </summary>
    public bool IsTimestampTz(SqlMemberExpression member)
        => SourceOf(member.SourceAlias).Columns.TryGetValue(member.Member, out var column)
            && column.DateTimeStorage == DateTimeColumnStorage.TimestampTz;

    private Source SourceOf(string? alias)
    {
        if (alias is null) return _from;
        if (_aliased.TryGetValue(alias, out var source)) return source;
        throw new InvalidOperationException("No source for alias " + alias + ".");
    }

    private sealed class Source
    {
        public Source(EntityMapModel map, string entityDisplay)
        {
            Map = map;
            EntityDisplay = entityDisplay;
            foreach (var column in map.Columns)
                Columns[column.Path] = column;
        }

        public EntityMapModel Map { get; }

        public string EntityDisplay { get; }

        // By member path: the property name, or Owner.Member for a member of an owned object.
        public Dictionary<string, ColumnMapModel> Columns { get; } = new(StringComparer.Ordinal);
    }
}
