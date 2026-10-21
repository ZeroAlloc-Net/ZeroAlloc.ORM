using System;
using System.Text;

namespace ZeroAlloc.ORM.Generator.Linq;

/// <summary>
/// Renders statements as PostgreSQL. Every identifier is double-quoted with <c>"</c> doubled, so
/// EF's PascalCase names and reserved words such as <c>Order</c> work as written. Values appear
/// only as <c>@name</c> parameters or as inlined compile-time literals.
/// </summary>
internal sealed class PostgresSqlRenderer : ISqlRenderer
{
    public static readonly PostgresSqlRenderer Instance = new();

    private PostgresSqlRenderer()
    {
    }

    public string Render(SqlStatement statement, SqlNameScope names)
    {
        var sb = new StringBuilder();
        switch (statement)
        {
            case SqlSelectStatement select:
                RenderSelect(sb, select, names);
                break;
            case SqlAggregateStatement aggregate:
                RenderAggregate(sb, aggregate, names);
                break;
            case SqlScalarAggregateStatement scalar:
                RenderScalarAggregate(sb, scalar, names);
                break;
            default:
                throw new InvalidOperationException("No PostgreSQL rendering for " + statement.GetType().Name + ".");
        }
        return sb.ToString();
    }

    private static void RenderSelect(StringBuilder sb, SqlSelectStatement select, SqlNameScope names)
    {
        sb.Append("SELECT ");
        AppendList(sb, select.Projection, names);
        RenderFromWhere(sb, select, names);
        if (select.GroupBy.Length > 0)
        {
            sb.Append(" GROUP BY ");
            AppendList(sb, select.GroupBy, names);
        }
        RenderOrdering(sb, select, names);
        RenderPaging(sb, select.Paging, names);
    }

    // count(*) reads the whole WHERE result, and a LIMIT next to it would limit the one row
    // the count returns. So a paged count counts a paged subquery:
    //   SELECT count(*)::int FROM "T" WHERE ...
    //   SELECT count(*)::int FROM (SELECT 1 FROM "T" WHERE ... LIMIT @take OFFSET @skip) AS q
    // EXISTS reads its subquery's rows, so the paging stays inside it:
    //   SELECT EXISTS (SELECT 1 FROM "T" WHERE ... LIMIT @take OFFSET @skip)
    private static void RenderAggregate(StringBuilder sb, SqlAggregateStatement aggregate, SqlNameScope names)
    {
        var source = aggregate.Source;
        if (aggregate.Aggregate == SqlAggregate.Any)
        {
            sb.Append("SELECT EXISTS (SELECT 1");
            RenderFromWhere(sb, source, names);
            RenderPaging(sb, source.Paging, names);
            sb.Append(')');
            return;
        }

        sb.Append(aggregate.Aggregate == SqlAggregate.Count ? "SELECT count(*)::int" : "SELECT count(*)");
        if (source.Paging.Limit is null && source.Paging.Offset is null)
        {
            RenderFromWhere(sb, source, names);
            return;
        }
        sb.Append(" FROM (SELECT 1");
        RenderFromWhere(sb, source, names);
        RenderPaging(sb, source.Paging, names);
        sb.Append(") AS q");
    }

    // As a paged count, a paged aggregate reads a paged subquery. The subquery selects the
    // aggregated column, so the call outside names it the same way, and keeps the ORDER BY,
    // which picks the rows of the page:
    //   SELECT COALESCE(sum("X"), 0)::int FROM "T" WHERE ...
    //   SELECT COALESCE(sum("X"), 0)::int FROM (SELECT "X" FROM "T" WHERE ... ORDER BY ... LIMIT @take) AS q
    private static void RenderScalarAggregate(StringBuilder sb, SqlScalarAggregateStatement scalar, SqlNameScope names)
    {
        var source = scalar.Source;
        sb.Append("SELECT ");
        RenderExpression(sb, scalar.Call, names);
        if (source.Paging.Limit is null && source.Paging.Offset is null)
        {
            RenderFromWhere(sb, source, names);
            return;
        }
        sb.Append(" FROM (SELECT ");
        // The subquery selects the bare column; a cast on the argument applies outside it.
        if (scalar.Call.Argument is SqlCastExpression { Operand: var column })
            RenderExpression(sb, column, names);
        else if (scalar.Call.Argument is { } argument)
            RenderExpression(sb, argument, names);
        else
            sb.Append('1');
        RenderFromWhere(sb, source, names);
        RenderOrdering(sb, source, names);
        RenderPaging(sb, source.Paging, names);
        sb.Append(") AS q");
    }

    // `COALESCE(sum("X"), 0)::int`, `avg("X")::double precision`, `count(*)`.
    private static void RenderAggregateCall(StringBuilder sb, SqlAggregateCallExpression call, SqlNameScope names)
    {
        if (call.CoalesceToZero) sb.Append("COALESCE(");
        sb.Append(call.Function switch
        {
            SqlAggregateFunction.Count or SqlAggregateFunction.LongCount => "count(",
            SqlAggregateFunction.Sum => "sum(",
            SqlAggregateFunction.Min => "min(",
            SqlAggregateFunction.Max => "max(",
            _ => "avg(",
        });
        if (call.Argument is { } argument)
            RenderExpression(sb, argument, names);
        else
            sb.Append('*');
        sb.Append(')');
        if (call.CoalesceToZero) sb.Append(", 0)");
        if (call.ResultCast is { } cast) sb.Append("::").Append(cast);
    }

    // ` FROM "schema"."table" [WHERE a AND b]`.
    private static void RenderFromWhere(StringBuilder sb, SqlSelectStatement select, SqlNameScope names)
    {
        sb.Append(" FROM ");
        if (names.Schema is { } schema)
            AppendIdentifier(sb, schema).Append('.');
        AppendIdentifier(sb, names.Table);

        if (select.Predicates.Length > 0)
        {
            sb.Append(" WHERE ");
            for (var i = 0; i < select.Predicates.Length; i++)
            {
                if (i > 0) sb.Append(" AND ");
                RenderExpression(sb, select.Predicates[i], names);
            }
        }
    }

    private static void RenderOrdering(StringBuilder sb, SqlSelectStatement select, SqlNameScope names)
    {
        for (var i = 0; i < select.Ordering.Length; i++)
        {
            sb.Append(i == 0 ? " ORDER BY " : ", ");
            RenderExpression(sb, select.Ordering[i].Expression, names);
            if (select.Ordering[i].Descending) sb.Append(" DESC");
        }
    }

    private static void RenderPaging(StringBuilder sb, SqlPaging paging, SqlNameScope names)
    {
        if (paging.Limit is { } limit)
        {
            sb.Append(" LIMIT ");
            RenderExpression(sb, limit, names);
        }
        if (paging.Offset is { } offset)
        {
            sb.Append(" OFFSET ");
            RenderExpression(sb, offset, names);
        }
    }

    private static void AppendList(StringBuilder sb, Model.EquatableArray<SqlExpression> items, SqlNameScope names)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            RenderExpression(sb, items[i], names);
        }
    }

    // One case per SqlExpression record. A new expression form adds its case here.
    // AND and OR are parenthesised, and NOT wraps its operand, so the tree's grouping
    // survives whatever PostgreSQL's precedence would make of the flat text.
    private static void RenderExpression(StringBuilder sb, SqlExpression expression, SqlNameScope names)
    {
        switch (expression)
        {
            case SqlMemberExpression member:
                AppendIdentifier(sb, names.Column(member));
                break;
            case SqlParameterExpression parameter:
                sb.Append('@').Append(parameter.Name);
                break;
            case SqlLiteralExpression literal:
                AppendLiteral(sb, literal);
                break;
            case SqlEnumLiteralExpression enumLiteral:
                AppendEnumLiteral(sb, enumLiteral, byName: enumLiteral.StoresName, names);
                break;
            case SqlBinaryExpression binary:
                RenderBinary(sb, binary, names);
                break;
            case SqlRowComparisonExpression rows:
                RenderRowComparison(sb, rows, names);
                break;
            case SqlPagingCountExpression count:
                if (count.AtMostOne) sb.Append("LEAST(");
                sb.Append("GREATEST(");
                RenderExpression(sb, count.Count, names);
                sb.Append(", 0)");
                if (count.AtMostOne) sb.Append(", 1)");
                break;
            case SqlNotExpression { Operand: SqlBinaryExpression { Operator: SqlBinaryOperator.And or SqlBinaryOperator.Or } grouped }:
                // AND and OR bring their own parentheses.
                sb.Append("NOT ");
                RenderExpression(sb, grouped, names);
                break;
            case SqlNotExpression not:
                sb.Append("NOT (");
                RenderExpression(sb, not.Operand, names);
                sb.Append(')');
                break;
            case SqlIsNotTrueExpression { Operand: SqlBinaryExpression { Operator: SqlBinaryOperator.And or SqlBinaryOperator.Or } grouped }:
                RenderExpression(sb, grouped, names);
                sb.Append(" IS NOT TRUE");
                break;
            case SqlIsNotTrueExpression isNotTrue:
                sb.Append('(');
                RenderExpression(sb, isNotTrue.Operand, names);
                sb.Append(") IS NOT TRUE");
                break;
            case SqlIsNullExpression isNull:
                RenderExpression(sb, isNull.Operand, names);
                AppendCast(sb, isNull.Operand, isNull.Cast, names);
                sb.Append(isNull.Negated ? " IS NOT NULL" : " IS NULL");
                break;
            case SqlCastExpression cast:
                RenderExpression(sb, cast.Operand, names);
                AppendCast(sb, cast.Operand, cast.Type, names);
                break;
            case SqlLikeExpression like:
                RenderLike(sb, like, names);
                break;
            case SqlILikeExpression ilike:
                RenderExpression(sb, ilike.Operand, names);
                sb.Append(" ILIKE ");
                RenderExpression(sb, ilike.Pattern, names);
                break;
            case SqlAnyExpression any:
                RenderExpression(sb, any.Operand, names);
                sb.Append(" = ANY(");
                RenderExpression(sb, any.Array, names);
                sb.Append(')');
                break;
            case SqlAggregateCallExpression call:
                RenderAggregateCall(sb, call, names);
                break;
            case SqlDateTruncExpression dateTrunc:
                sb.Append("date_trunc('day', ");
                RenderExpression(sb, dateTrunc.Operand, names);
                sb.Append(')');
                break;
            case SqlConditionalExpression conditional:
                // IS NOT TRUE, not NOT: a condition that is NULL, as `p > 0` is for a null p,
                // is false in C# and skips the filter, where NOT (NULL) would drop every row.
                sb.Append("((");
                RenderExpression(sb, conditional.Condition, names);
                sb.Append(") IS NOT TRUE OR (");
                RenderExpression(sb, conditional.Predicate, names);
                sb.Append("))");
                break;
            default:
                throw new InvalidOperationException("No PostgreSQL rendering for " + expression.GetType().Name + ".");
        }
    }

    // `::type` after a parameter without a DbType facet. A name-bound enum gets the String facet
    // when bound, so it needs no cast.
    private static void AppendCast(StringBuilder sb, SqlExpression operand, string? cast, SqlNameScope names)
    {
        if (cast is not null
            && !(operand is SqlParameterExpression bound && names.NameBoundParameters.Contains(bound.Name)))
        {
            sb.Append("::").Append(cast);
        }
    }

    private static void RenderBinary(StringBuilder sb, SqlBinaryExpression binary, SqlNameScope names)
    {
        if (binary.Operator is SqlBinaryOperator.And or SqlBinaryOperator.Or)
        {
            sb.Append('(');
            RenderExpression(sb, binary.Left, names);
            sb.Append(binary.Operator == SqlBinaryOperator.And ? " AND " : " OR ");
            RenderExpression(sb, binary.Right, names);
            sb.Append(')');
            return;
        }

        if (binary.Operator is not (SqlBinaryOperator.Equal or SqlBinaryOperator.NotEqual))
        {
            ReportOrderedNameColumn(binary.Left, names);
            ReportOrderedNameColumn(binary.Right, names);
        }

        RenderComparand(sb, binary.Left, binary.Right, names);
        sb.Append(ComparisonText(binary.Operator));
        RenderComparand(sb, binary.Right, binary.Left, names);
    }

    private static string ComparisonText(SqlBinaryOperator comparison) => comparison switch
    {
        SqlBinaryOperator.Equal => " = ",
        SqlBinaryOperator.NotEqual => " <> ",
        SqlBinaryOperator.LessThan => " < ",
        SqlBinaryOperator.LessThanOrEqual => " <= ",
        SqlBinaryOperator.GreaterThan => " > ",
        _ => " >= ",
    };

    // `("A", "B") > (@a, @b)`: PostgreSQL compares the rows item by item, left to right. Each
    // pair renders as a comparison would, so an enum constant takes its column's storage, and
    // a column stored by name is reported: the row orders by it.
    private static void RenderRowComparison(StringBuilder sb, SqlRowComparisonExpression rows, SqlNameScope names)
    {
        for (var i = 0; i < rows.Left.Length; i++)
        {
            ReportOrderedNameColumn(rows.Left[i], names);
            ReportOrderedNameColumn(rows.Right[i], names);
        }
        RenderRow(sb, rows.Left, rows.Right, names);
        sb.Append(ComparisonText(rows.Operator));
        RenderRow(sb, rows.Right, rows.Left, names);
    }

    private static void RenderRow(StringBuilder sb, Model.EquatableArray<SqlExpression> items, Model.EquatableArray<SqlExpression> others, SqlNameScope names)
    {
        sb.Append('(');
        for (var i = 0; i < items.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            RenderComparand(sb, items[i], others[i], names);
        }
        sb.Append(')');
    }

    // <, >, <= and >= on a column that stores enum member names would order the names.
    private static void ReportOrderedNameColumn(SqlExpression side, SqlNameScope names)
    {
        if (side is SqlMemberExpression member && names.IsEnumAsString(member))
        {
            names.Report(LinqDiagnostics.UnsupportedAt(
                "Ordering '" + member.Member + "', whose column stores enum member names,",
                member.Location));
        }
    }

    // An enum constant compared with a column the map stores by name, or with a parameter that
    // binds by name, renders as the name.
    private static void RenderComparand(StringBuilder sb, SqlExpression side, SqlExpression other, SqlNameScope names)
    {
        if (side is SqlEnumLiteralExpression enumLiteral)
        {
            var byName = enumLiteral.StoresName || other switch
            {
                SqlMemberExpression member => names.IsEnumAsString(member),
                SqlParameterExpression parameter => names.NameBoundParameters.Contains(parameter.Name),
                _ => false,
            };
            AppendEnumLiteral(sb, enumLiteral, byName, names);
            return;
        }
        RenderExpression(sb, side, names);
    }

    // `"Col" LIKE '%' || <esc>(value) || '%' ESCAPE '\'`, with `%` only on the open sides.
    // <esc> escapes the value's backslashes, then its wildcards, so it matches literally.
    private static void RenderLike(StringBuilder sb, SqlLikeExpression like, SqlNameScope names)
    {
        RenderExpression(sb, like.Operand, names);
        sb.Append(" LIKE ");
        if (like.Match != SqlLikeMatch.StartsWith) sb.Append("'%' || ");
        sb.Append("replace(replace(replace(");
        RenderExpression(sb, like.Value, names);
        sb.Append(@",'\','\\'),'%','\%'),'_','\_')");
        if (like.Match != SqlLikeMatch.EndsWith) sb.Append(" || '%'");
        sb.Append(@" ESCAPE '\'");
    }

    private static void AppendEnumLiteral(StringBuilder sb, SqlEnumLiteralExpression literal, bool byName, SqlNameScope names)
    {
        if (!byName)
        {
            sb.Append(literal.Number);
            return;
        }
        if (literal.Name is null)
        {
            names.Report(LinqDiagnostics.UnsupportedAt(
                "'" + literal.Number + "', which names no enum member, compared with a value stored by member name,",
                literal.Location));
            sb.Append(literal.Number);
            return;
        }
        sb.Append('\'').Append(literal.Name).Append('\'');
    }

    private static void AppendLiteral(StringBuilder sb, SqlLiteralExpression literal)
    {
        switch (literal.Kind)
        {
            case SqlLiteralKind.Null:
                sb.Append("NULL");
                break;
            case SqlLiteralKind.Boolean:
                sb.Append(string.Equals(literal.Value, "true", StringComparison.Ordinal) ? "TRUE" : "FALSE");
                break;
            case SqlLiteralKind.String:
                sb.Append('\'').Append(literal.Value.Replace("'", "''")).Append('\'');
                break;
            case SqlLiteralKind.Real:
                sb.Append(literal.Value).Append("::real");
                break;
            default:
                sb.Append(literal.Value);
                break;
        }
    }

    private static StringBuilder AppendIdentifier(StringBuilder sb, string identifier)
        => sb.Append('"').Append(identifier.Replace("\"", "\"\"")).Append('"');
}
