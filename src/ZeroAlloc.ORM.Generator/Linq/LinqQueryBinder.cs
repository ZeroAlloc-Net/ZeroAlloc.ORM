using System;
using System.Collections.Immutable;
using System.Linq;
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

        // Enum storage first: the render writes an enum constant by name next to a name-bound
        // parameter. A conflict is reported after the render's own diagnostics, such as ZAO105.
        var names = new SqlNameScope(map, DisplayName(from.EntityTypeFullName));
        var storage = EnumParameterStorage.Decide(statement, names);
        names.NameBoundParameters = storage.ByName.ToImmutableHashSet(StringComparer.Ordinal);
        var sql = PostgresSqlRenderer.Instance.Render(statement, names);
        var diagnostics = names.Diagnostics;
        if (!diagnostics.IsEmpty)
            return new LinqBoundQuery(string.Empty, default, default, new EquatableArray<DiagnosticInfo>(diagnostics));

        if (!storage.Conflicts.IsEmpty)
            return new LinqBoundQuery(string.Empty, default, default, new EquatableArray<DiagnosticInfo>(storage.Conflicts));

        return new LinqBoundQuery(
            sql,
            EnumAsStringOrdinals(statement, names, storage.ByName),
            new EquatableArray<string>(storage.ByName),
            default);
    }

    // The statement's table; a terminal's is its inner SELECT's.
    private static SqlTableSource SourceOf(SqlStatement statement) => statement switch
    {
        SqlSelectStatement select => select.From,
        SqlAggregateStatement aggregate => aggregate.Source.From,
        SqlScalarAggregateStatement scalar => scalar.Source.From,
        _ => throw new InvalidOperationException("No table source for " + statement.GetType().Name + "."),
    };

    // SELECT positions that read a member name: an EnumAsString column, or an enum parameter
    // that binds by name, which the database hands back as the text it was sent.
    private static EquatableArray<int> EnumAsStringOrdinals(
        SqlStatement statement,
        SqlNameScope names,
        ImmutableArray<string> nameBoundParameters)
    {
        if (statement is not SqlSelectStatement select) return default;
        var ordinals = ImmutableArray.CreateBuilder<int>();
        for (var i = 0; i < select.Projection.Length; i++)
        {
            var byName = select.Projection[i] switch
            {
                SqlMemberExpression member => names.IsEnumAsString(member),
                SqlParameterExpression parameter => nameBoundParameters.Contains(parameter.Name, StringComparer.Ordinal),
                SqlCastExpression { Operand: SqlParameterExpression parameter } => nameBoundParameters.Contains(parameter.Name, StringComparer.Ordinal),
                _ => false,
            };
            if (byName) ordinals.Add(i);
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
        => new(string.Empty, default, default, new EquatableArray<DiagnosticInfo>(ImmutableArray.Create(diagnostic)));
}
