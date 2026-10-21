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
            default:
                throw new InvalidOperationException("No PostgreSQL rendering for " + statement.GetType().Name + ".");
        }
        return sb.ToString();
    }

    private static void RenderSelect(StringBuilder sb, SqlSelectStatement select, SqlNameScope names)
    {
        sb.Append("SELECT ");
        AppendList(sb, select.Projection, names);
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

        RenderOrdering(sb, select, names);
        RenderPaging(sb, select.Paging, names);
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
            default:
                throw new InvalidOperationException("No PostgreSQL rendering for " + expression.GetType().Name + ".");
        }
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
            default:
                sb.Append(literal.Value);
                break;
        }
    }

    private static StringBuilder AppendIdentifier(StringBuilder sb, string identifier)
        => sb.Append('"').Append(identifier.Replace("\"", "\"\"")).Append('"');
}
