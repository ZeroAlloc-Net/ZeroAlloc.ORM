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
/// Resolves the entity members a statement names to the mapped table and columns. A member that
/// is not a mapped column is recorded as ZAO105, so one render both produces the SQL and checks
/// every name in it. Used for one render, never cached.
/// </summary>
internal sealed class SqlNameScope
{
    private readonly EntityMapModel _map;
    private readonly string _entityDisplay;
    private readonly Dictionary<string, ColumnMapModel> _columns = new(StringComparer.Ordinal);
    private readonly List<DiagnosticInfo> _diagnostics = new();

    public SqlNameScope(EntityMapModel map, string entityDisplay)
    {
        _map = map;
        _entityDisplay = entityDisplay;
        foreach (var column in map.Columns)
            _columns[column.Property] = column;
    }

    public string Table => _map.Table;

    public string? Schema => _map.Schema;

    public ImmutableArray<DiagnosticInfo> Diagnostics => _diagnostics.ToImmutableArray();

    /// <summary>The member's column, or the member name after recording ZAO105.</summary>
    public string Column(SqlMemberExpression member)
    {
        if (_columns.TryGetValue(member.Member, out var column)) return column.Column;
        _diagnostics.Add(LinqDiagnostics.UnmappedMember(member.Member, _entityDisplay, member.Location));
        return member.Member;
    }

    /// <summary>Records a diagnostic the render found, such as an enum value with no name to compare by.</summary>
    public void Report(DiagnosticInfo diagnostic) => _diagnostics.Add(diagnostic);

    /// <summary>Whether the member maps to a column that stores an enum as its member name.</summary>
    public bool IsEnumAsString(SqlMemberExpression member)
        => _columns.TryGetValue(member.Member, out var column) && column.EnumAsString;
}
