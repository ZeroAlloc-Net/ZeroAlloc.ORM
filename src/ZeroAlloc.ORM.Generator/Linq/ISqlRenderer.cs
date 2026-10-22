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
/// alias. Used for one render, never cached.
/// </summary>
internal sealed class SqlNameScope
{
    private readonly Source _from;
    private readonly Dictionary<string, Source> _aliased = new(StringComparer.Ordinal);
    private readonly List<DiagnosticInfo> _diagnostics = new();
    private readonly HashSet<DiagnosticInfo> _unmapped = new();

    public SqlNameScope(EntityMapModel map, string entityDisplay, string? alias = null)
    {
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
    /// SQL names of the enum parameters that bind as their member name, decided by
    /// <see cref="EnumParameterStorage"/> before the render.
    /// </summary>
    public ImmutableHashSet<string> NameBoundParameters { get; set; } = ImmutableHashSet<string>.Empty;

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
