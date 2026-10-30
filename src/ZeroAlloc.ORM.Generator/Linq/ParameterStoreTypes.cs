using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// The PostgreSQL type a parameter binds with explicitly, rather than as the type Npgsql infers
/// from its value: the store type of a column the parameter is compared with or set into.
/// </summary>
internal enum SqlStoreType
{
    /// <summary>
    /// A <c>jsonb</c> column: one whose map says <c>Json = true</c>, or whose property or record
    /// constructor parameter <c>[Json]</c> marks. Left as text, PostgreSQL rejects
    /// <c>jsonb = text</c> at run time, 42883.
    /// </summary>
    Jsonb,

    /// <summary>
    /// A <c>DateTime</c> column its map declares a <c>timestamp</c>. Left to Npgsql, a Kind Utc
    /// value would be sent as a <c>timestamptz</c>, which PostgreSQL shifts through the session's
    /// <c>TimeZone</c> to meet the column.
    /// </summary>
    Timestamp,

    /// <summary>
    /// A <c>DateTime</c> column its map declares a <c>timestamptz</c>. Left to Npgsql, a Kind
    /// Local or Unspecified value would be sent as a <c>timestamp</c>, which PostgreSQL shifts
    /// through the session's <c>TimeZone</c> to meet the column.
    /// </summary>
    TimestampTz,
}

/// <summary>A parameter that binds with an explicit PostgreSQL type, at its first use as one.</summary>
/// <param name="Parameter">The parameter, at its first use that decides the type.</param>
/// <param name="StoreType">The type it binds with.</param>
internal sealed record SqlTypedParameter(SqlParameterExpression Parameter, SqlStoreType StoreType)
{
    /// <summary>The <c>NpgsqlDbType</c> member the parameter binds with.</summary>
    public string NpgsqlDbType => StoreType switch
    {
        SqlStoreType.Jsonb => "Jsonb",
        SqlStoreType.Timestamp => "Timestamp",
        _ => "TimestampTz",
    };

    /// <summary>The PostgreSQL type, as a cast names it.</summary>
    public string PostgresType => ParameterStoreTypes.PostgresName(StoreType);
}

/// <summary>
/// The parameters a statement compares with, or sets into, a column whose store type the map
/// knows, and so binds with that type explicitly:
/// <list type="bullet">
/// <item>a <c>jsonb</c> column, as a <c>[Json]</c> parameter of a <c>[Query]</c> binds;</item>
/// <item>a <c>DateTime</c> column its map declares a <c>timestamp</c> or a <c>timestamptz</c>,
/// which EF Core's Npgsql provider binds the same way. Npgsql then rejects a value whose Kind does
/// not fit the column, rather than PostgreSQL shifting it through the session's <c>TimeZone</c>.
/// A column whose storage is Default keeps Npgsql's choice from the value's Kind.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// The conditions of every statement are walked, a join's and an IN subquery's included, and an
/// UPDATE's setters.
/// </para>
/// <para>
/// A parameter binds one way for the whole method, so one that meets two store types, such as a
/// <c>timestamp</c> column and a <c>timestamptz</c> one, is ZAO102. jsonb is strict as well: a
/// parameter that also meets a text column, sits in another form such as LIKE, or is set into a
/// text column is ZAO102. A <c>DateTime</c> parameter may meet any other use beside its column,
/// such as a projection or a column whose storage is Default, which the explicit type serves as
/// well as an inferred one. jsonb equality compares the documents, not their text:
/// <c>{"a":1}</c> equals <c>{ "a": 1 }</c>. Decided when the statement is bound, since only then
/// is the map known, and before the render, whose null-test casts follow the decided type.
/// </para>
/// </remarks>
internal static class ParameterStoreTypes
{
    public static string PostgresName(SqlStoreType storeType) => storeType switch
    {
        SqlStoreType.Jsonb => "jsonb",
        SqlStoreType.Timestamp => "timestamp",
        _ => "timestamptz",
    };

    public static ImmutableArray<SqlTypedParameter> Decide(
        ImmutableArray<(SqlStatement Statement, SqlNameScope Names)> statements,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        // Each parameter's typed uses in walk order, the first of each type kept, and the
        // parameters any other use names.
        var typed = new List<(SqlParameterExpression Parameter, SqlStoreType StoreType)>();
        var other = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (statement, scope) in statements)
        {
            var names = scope;
            switch (statement)
            {
                case SqlSelectStatement select:
                    VisitSelect(select);
                    break;
                case SqlAggregateStatement aggregate:
                    VisitSelect(aggregate.Source);
                    break;
                case SqlScalarAggregateStatement scalar:
                    VisitSelect(scalar.Source);
                    break;
                case SqlDeleteStatement delete:
                    foreach (var predicate in delete.Predicates) Visit(predicate);
                    break;
                case SqlUpdateStatement update:
                    // A parameter set into a column binds as the column's store type, as one
                    // compared with it does; set into any other column, it is an untyped use.
                    foreach (var setter in update.Setters)
                    {
                        var storeType = setter.ColumnJson ? SqlStoreType.Jsonb : StoreTypeOf(setter.Column);
                        if (storeType is { } type && setter.Value is SqlParameterExpression value)
                            typed.Add((value, type));
                        else
                            Other(setter.Value);
                    }
                    foreach (var predicate in update.Predicates) Visit(predicate);
                    break;
                default:
                    throw new InvalidOperationException("No parameter store type walk for " + statement.GetType().Name + ".");
            }

            void VisitSelect(SqlSelectStatement select)
            {
                foreach (var item in select.Projection) Other(item);
                foreach (var join in select.Joins)
                {
                    Visit(join.On);
                    foreach (var predicate in join.SourcePredicates) Visit(predicate);
                }
                foreach (var predicate in select.Predicates) Visit(predicate);
                foreach (var key in select.GroupBy) Other(key);
                foreach (var ordering in select.Ordering) Other(ordering.Expression);
                if (select.Paging.Limit is { } limit) Other(limit);
                if (select.Paging.Offset is { } offset) Other(offset);
            }

            void Visit(SqlExpression expression)
            {
                switch (expression)
                {
                    case SqlBinaryExpression { Operator: SqlBinaryOperator.And or SqlBinaryOperator.Or } logical:
                        Visit(logical.Left);
                        Visit(logical.Right);
                        break;
                    case SqlBinaryExpression comparison:
                        Compare(comparison.Left, comparison.Right);
                        break;
                    case SqlRowComparisonExpression rows:
                        for (var i = 0; i < rows.Left.Length; i++) Compare(rows.Left[i], rows.Right[i]);
                        break;
                    // `p IS NULL` types nothing: it sits beside a comparison for C#'s null semantics.
                    case SqlIsNullExpression { Operand: SqlParameterExpression or SqlMemberExpression }:
                        break;
                    case SqlNotExpression not:
                        Visit(not.Operand);
                        break;
                    case SqlIsNotTrueExpression isNotTrue:
                        Visit(isNotTrue.Operand);
                        break;
                    case SqlConditionalExpression conditional:
                        Visit(conditional.Condition);
                        Visit(conditional.Predicate);
                        break;
                    case SqlInSubqueryExpression subquery:
                        Other(subquery.Operand);
                        var outer = names;
                        names = outer.Subquery(subquery.Subquery);
                        VisitSelect(subquery.Subquery);
                        names = outer;
                        break;
                    default:
                        Other(expression);
                        break;
                }
            }

            void Compare(SqlExpression left, SqlExpression right)
            {
                switch (left, right)
                {
                    case (SqlParameterExpression first, SqlMemberExpression firstMember) when StoreTypeOf(firstMember) is { } firstType:
                        typed.Add((first, firstType));
                        break;
                    case (SqlMemberExpression secondMember, SqlParameterExpression second) when StoreTypeOf(secondMember) is { } secondType:
                        typed.Add((second, secondType));
                        break;
                    default:
                        Other(left);
                        Other(right);
                        break;
                }
            }

            // The store type of a column the map knows one for: jsonb, or a declared DateTime storage.
            SqlStoreType? StoreTypeOf(SqlMemberExpression member)
                => member.Json || names.IsJson(member) ? SqlStoreType.Jsonb : DateTimeStoreTypeOf(member);

            SqlStoreType? DateTimeStoreTypeOf(SqlMemberExpression member) => names.DateTimeStorageOf(member) switch
            {
                DateTimeColumnStorage.Timestamp => SqlStoreType.Timestamp,
                DateTimeColumnStorage.TimestampTz => SqlStoreType.TimestampTz,
                _ => null,
            };
        }

        // In the order of each parameter's first typed use. A parameter that meets two store
        // types, or a jsonb one and any other use, binds one way only and is reported.
        var bound = ImmutableArray.CreateBuilder<SqlTypedParameter>();
        var decided = new Dictionary<string, SqlTypedParameter>(StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (parameter, storeType) in typed)
        {
            if (!decided.TryGetValue(parameter.Name, out var first))
            {
                first = new SqlTypedParameter(parameter, storeType);
                decided.Add(parameter.Name, first);
                bound.Add(first);
                if (storeType == SqlStoreType.Jsonb && other.Contains(parameter.Name) && reported.Add(parameter.Name))
                {
                    diagnostics.Add(LinqDiagnostics.UnsupportedAt(
                        "Parameter '" + parameter.ClrName + "', which binds as jsonb for a jsonb column,"
                            + " used against something other than a jsonb column as well,",
                        parameter.Location));
                }
            }
            else if (first.StoreType != storeType && reported.Add(parameter.Name))
            {
                diagnostics.Add(LinqDiagnostics.UnsupportedAt(
                    "Parameter '" + parameter.ClrName + "', which binds as " + first.PostgresType + " for a "
                        + first.PostgresType + " column, used against a " + PostgresName(storeType) + " column as well,",
                    parameter.Location));
            }
        }
        return bound.ToImmutable();

        void Other(SqlExpression expression) => other.UnionWith(SqlParameterReferences.Collect(new[] { expression }));
    }
}
