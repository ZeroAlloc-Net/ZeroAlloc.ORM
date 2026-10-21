using System;
using System.Collections.Immutable;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// Binds a <see cref="LinqTranslation"/> to the entity maps and renders it. Runs after the
/// per-method read, combined with the collected maps, so a map edit rebinds every query without
/// re-reading it, and an unrelated edit leaves both cached.
/// </summary>
internal static class LinqQueryBinder
{
    public static LinqBoundQuery Bind(SqlStatement statement, EquatableArray<EntityMapModel> maps)
    {
        var from = SourceOf(statement);
        var map = FindMap(maps, from.EntityTypeFullName);
        if (map is null)
        {
            return Failed(LinqDiagnostics.UnmappedEntity(DisplayName(from.EntityTypeFullName), from.Location));
        }

        var names = new SqlNameScope(map, DisplayName(from.EntityTypeFullName));
        var sql = PostgresSqlRenderer.Instance.Render(statement, names);
        var diagnostics = names.Diagnostics;
        if (!diagnostics.IsEmpty)
            return new LinqBoundQuery(string.Empty, default, new EquatableArray<DiagnosticInfo>(diagnostics));

        return new LinqBoundQuery(sql, EnumAsStringOrdinals(statement, names), default);
    }

    // The statement's table. A wrapping statement a later task adds names its inner SELECT's.
    private static SqlTableSource SourceOf(SqlStatement statement) => statement switch
    {
        SqlSelectStatement select => select.From,
        _ => throw new InvalidOperationException("No table source for " + statement.GetType().Name + "."),
    };

    private static EquatableArray<int> EnumAsStringOrdinals(SqlStatement statement, SqlNameScope names)
    {
        if (statement is not SqlSelectStatement select) return default;
        var ordinals = ImmutableArray.CreateBuilder<int>();
        for (var i = 0; i < select.Projection.Length; i++)
        {
            if (select.Projection[i] is SqlMemberExpression member && names.IsEnumAsString(member))
                ordinals.Add(i);
        }
        return new EquatableArray<int>(ordinals.ToImmutable());
    }

    private static EntityMapModel? FindMap(EquatableArray<EntityMapModel> maps, string entityTypeFullName)
    {
        foreach (var map in maps)
        {
            if (string.Equals(map.EntityTypeFullName, entityTypeFullName, StringComparison.Ordinal))
                return map;
        }
        return null;
    }

    // `global::TestApp.TaskEntity` as a message shows it: `TestApp.TaskEntity`.
    private static string DisplayName(string entityTypeFullName)
        => entityTypeFullName.StartsWith("global::", StringComparison.Ordinal)
            ? entityTypeFullName.Substring("global::".Length)
            : entityTypeFullName;

    private static LinqBoundQuery Failed(DiagnosticInfo diagnostic)
        => new(string.Empty, default, new EquatableArray<DiagnosticInfo>(ImmutableArray.Create(diagnostic)));
}
