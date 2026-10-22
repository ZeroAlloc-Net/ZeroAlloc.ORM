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
    private ImmutableHashSet<string> _nameBoundParameters = ImmutableHashSet<string>.Empty;

    /// <param name="map">The FROM entity's map.</param>
    /// <param name="entityDisplay">The FROM entity as messages show it.</param>
    /// <param name="alias">The FROM entity's alias, when the statement has a join.</param>
    /// <param name="maps">Every entity map, which an IN subquery's sources resolve through.</param>
    public SqlNameScope(EntityMapModel map, string entityDisplay, string? alias = null, EquatableArray<EntityMapModel> maps = default)
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
        if (source.Columns.TryGetValue(member.Member, out var column)) return column.Column;
        var unmapped = LinqDiagnostics.UnmappedMember(member.Member, source.EntityDisplay, member.Location);
        if (_unmapped.Add(unmapped)) _diagnostics.Add(unmapped);
        return member.Member;
    }

    /// <summary>
    /// The columns of the owned object the test names, in map order; or, after recording ZAO105
    /// once per owner and location, the owner's name alone when the map does not own it.
    /// </summary>
    public ImmutableArray<string> OwnedColumns(SqlOwnedIsNullExpression test)
    {
        var source = SourceOf(test.SourceAlias);
        var columns = ImmutableArray.CreateBuilder<string>();
        foreach (var column in source.Map.Columns)
        {
            if (string.Equals(column.Owner, test.Owner, StringComparison.Ordinal))
                columns.Add(column.Column);
        }
        if (columns.Count > 0) return columns.ToImmutable();

        var unmapped = LinqDiagnostics.UnmappedMember(test.Owner, source.EntityDisplay, test.Location);
        if (_unmapped.Add(unmapped)) _diagnostics.Add(unmapped);
        return ImmutableArray.Create(test.Owner);
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
    /// join, and each joined source under its own. A source without a map records ZAO104 and
    /// binds through an empty one, so the render goes on and the statement fails on the
    /// diagnostic. One scope per subquery, so a subquery walked twice resolves alike.
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

    // The map of a subquery's source: from every map the binder holds, else from a source this
    // scope or an enclosing one already reads.
    private EntityMapModel MapOf(SqlTableSource source)
    {
        if (LinqQueryBinder.FindMap(_maps, source.EntityTypeFullName) is { } map) return map;
        for (var scope = this; scope is not null; scope = scope._parent)
        {
            if (string.Equals(scope._from.Map.EntityTypeFullName, source.EntityTypeFullName, StringComparison.Ordinal))
                return scope._from.Map;
            foreach (var aliased in scope._aliased.Values)
            {
                if (string.Equals(aliased.Map.EntityTypeFullName, source.EntityTypeFullName, StringComparison.Ordinal))
                    return aliased.Map;
            }
        }

        var display = LinqQueryBinder.DisplayName(source.EntityTypeFullName);
        var unmapped = LinqDiagnostics.UnmappedEntity(display, source.Location);
        if (_unmapped.Add(unmapped)) _diagnostics.Add(unmapped);
        return new EntityMapModel(source.EntityTypeFullName, display, Schema: null, EquatableArray<ColumnMapModel>.Empty);
    }

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
