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
        => Bind(new EquatableArray<SqlStatement>(ImmutableArray.Create(statement)), maps);

    // Several statements, a tuple companion's, bind as one: each in its own name scope, and an
    // enum parameter binds one way for all of them, since every batch command binds it alike.
    public static LinqBoundQuery Bind(EquatableArray<SqlStatement> statements, EquatableArray<EntityMapModel> maps)
        => Bind(statements, maps, default);

    // `lifted` are the translation's projected members of possibly optional owned objects: ZAO107
    // for each whose owner the map declares owned.
    public static LinqBoundQuery Bind(
        EquatableArray<SqlStatement> statements,
        EquatableArray<EntityMapModel> maps,
        EquatableArray<LinqLiftedProjection> lifted)
        => Bind(statements, maps, lifted, default, default);

    // `xidParameters` are the uint parameters the reader binds as xid, compared with xmin; a
    // member they meet must map to xmin. `xminProjections` are the projected uint members: ZAO107
    // for each whose column is not xmin, the one uint a row reads.
    public static LinqBoundQuery Bind(
        EquatableArray<SqlStatement> statements,
        EquatableArray<EntityMapModel> maps,
        EquatableArray<LinqLiftedProjection> lifted,
        EquatableArray<string> xidParameters,
        EquatableArray<LinqXminProjection> xminProjections)
    {
        var builder = ImmutableArray.CreateBuilder<(SqlStatement Statement, SqlNameScope Names)>(statements.Length);
        foreach (var statement in statements)
        {
            var (from, alias, joins) = SourcesOf(statement);
            var names = Scope(from, alias, joins, maps, out var unmapped);
            if (names is null) return Failed(unmapped!);
            foreach (var subquery in SqlSubqueries.Collect(statement))
            {
                if (UnmappedSource(subquery, maps) is { } missing) return Failed(missing);
            }
            builder.Add((statement, names));
        }
        var scopes = builder.MoveToImmutable();

        // Enum storage first: the render writes an enum constant by name next to a name-bound
        // parameter. A conflict is reported after the render's own diagnostics, such as ZAO105.
        var storage = EnumParameterStorage.Decide(scopes);
        var byName = storage.ByName.ToImmutableHashSet(StringComparer.Ordinal);
        var xid = xidParameters.ToImmutableHashSet(StringComparer.Ordinal);
        var sql = new string[statements.Length];
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        for (var i = 0; i < statements.Length; i++)
        {
            var names = scopes[i].Names;
            names.NameBoundParameters = byName;
            names.XidParameters = xid;
            if (statements[i] is SqlSelectStatement select) names.Projected(select.Projection);
            sql[i] = PostgresSqlRenderer.Instance.Render(statements[i], names);
            diagnostics.AddRange(names.Diagnostics);
        }
        foreach (var projection in xminProjections)
        {
            if (scopes[projection.Statement].Names.MappedColumn(projection.Member) is { } column && !SqlNameScope.IsXminColumn(column))
            {
                diagnostics.Add(LinqDiagnostics.UnsupportedProjectionAt(
                    "'" + projection.Member.Member + "' reads a uint, which only xmin reads as, but its column is " + column,
                    projection.Location));
            }
        }
        foreach (var projection in lifted)
        {
            if (scopes[projection.Statement].Names.IsOwned(projection.Owner, projection.SourceAlias))
                diagnostics.Add(LinqDiagnostics.UnsupportedProjectionAt(projection.Reason, projection.Location));
        }
        var jsonParameters = ImmutableArray.CreateBuilder<SqlParameterExpression>();
        for (var i = 0; i < statements.Length; i++)
        {
            if (statements[i] is SqlUpdateStatement update)
                jsonParameters.AddRange(JsonSetters.Check(update, scopes[i].Names, diagnostics));
        }
        jsonParameters.AddRange(JsonComparisons.Check(scopes, diagnostics));
        if (diagnostics.Count > 0)
            return new LinqBoundQuery(string.Empty, default, default, new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutable()));

        if (!storage.Conflicts.IsEmpty)
            return new LinqBoundQuery(string.Empty, default, default, new EquatableArray<DiagnosticInfo>(storage.Conflicts));

        var ordinals = ImmutableArray.CreateBuilder<EquatableArray<int>>(statements.Length);
        for (var i = 0; i < statements.Length; i++)
            ordinals.Add(EnumAsStringOrdinals(statements[i], scopes[i].Names, storage.ByName));
        return new LinqBoundQuery(
            string.Join(";\n", sql),
            new EquatableArray<EquatableArray<int>>(ordinals.MoveToImmutable()),
            new EquatableArray<string>(storage.ByName),
            default)
        {
            Statements = new EquatableArray<string>(ImmutableArray.Create(sql)),
            JsonParameters = new EquatableArray<SqlParameterExpression>(jsonParameters.ToImmutable()),
        };
    }

    // The statement's sources: a SELECT's FROM, alias and joins, a terminal's those of its inner
    // SELECT, and a DELETE's or an UPDATE's table, which has neither an alias nor a join.
    private static (SqlTableSource From, string? Alias, EquatableArray<SqlJoin> Joins) SourcesOf(SqlStatement statement) => statement switch
    {
        SqlSelectStatement select => (select.From, select.Alias, select.Joins),
        SqlAggregateStatement aggregate => (aggregate.Source.From, aggregate.Source.Alias, aggregate.Source.Joins),
        SqlScalarAggregateStatement scalar => (scalar.Source.From, scalar.Source.Alias, scalar.Source.Joins),
        SqlDeleteStatement delete => (delete.Table, null, default),
        SqlUpdateStatement update => (update.Table, null, default),
        _ => throw new InvalidOperationException("No table source for " + statement.GetType().Name + "."),
    };

    // One name scope over every source: the FROM entity, under its alias when there is a join,
    // and each joined entity under its own. Null with ZAO104 for the first source without a map.
    private static SqlNameScope? Scope(
        SqlTableSource from,
        string? alias,
        EquatableArray<SqlJoin> joins,
        EquatableArray<EntityMapModel> maps,
        out DiagnosticInfo? unmapped)
    {
        unmapped = null;
        if (FindMap(maps, from.EntityTypeFullName) is not { } map)
        {
            unmapped = LinqDiagnostics.UnmappedEntity(DisplayName(from.EntityTypeFullName), from.Location);
            return null;
        }

        var names = new SqlNameScope(map, DisplayName(from.EntityTypeFullName), alias, maps);
        foreach (var join in joins)
        {
            var joined = join.Source.EntityTypeFullName;
            if (FindMap(maps, joined) is not { } joinedMap)
            {
                unmapped = LinqDiagnostics.UnmappedEntity(DisplayName(joined), join.Source.Location);
                return null;
            }
            names.AddSource(join.Alias, joinedMap, DisplayName(joined));
        }
        return names;
    }

    // ZAO104 for the first source of an IN subquery without a map, its FROM or a joined one, as
    // Scope reports it for the statement's own; null when every source has one.
    private static DiagnosticInfo? UnmappedSource(SqlSelectStatement subquery, EquatableArray<EntityMapModel> maps)
    {
        if (FindMap(maps, subquery.From.EntityTypeFullName) is null)
            return LinqDiagnostics.UnmappedEntity(DisplayName(subquery.From.EntityTypeFullName), subquery.From.Location);
        foreach (var join in subquery.Joins)
        {
            if (FindMap(maps, join.Source.EntityTypeFullName) is null)
                return LinqDiagnostics.UnmappedEntity(DisplayName(join.Source.EntityTypeFullName), join.Source.Location);
        }
        return null;
    }

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

    internal static EntityMapModel? FindMap(EquatableArray<EntityMapModel> maps, string entityTypeFullName)
    {
        foreach (var map in maps)
        {
            if (string.Equals(map.EntityTypeFullName, entityTypeFullName, StringComparison.Ordinal))
                return map;
        }
        return null;
    }

    // `global::TestApp.TaskEntity` as a message shows it: `TestApp.TaskEntity`.
    internal static string DisplayName(string entityTypeFullName)
        => entityTypeFullName.StartsWith("global::", StringComparison.Ordinal)
            ? entityTypeFullName.Substring("global::".Length)
            : entityTypeFullName;

    private static LinqBoundQuery Failed(DiagnosticInfo diagnostic)
        => new(string.Empty, default, default, new EquatableArray<DiagnosticInfo>(ImmutableArray.Create(diagnostic)));
}
