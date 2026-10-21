using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

internal static partial class LinqQueryReader
{
    private const string SqlRowTypeName = "SqlRow";

    // Whether a comparison operator is one of SqlRow's, as in Sql.Row(a, b) > Sql.Row(x, y).
    private static bool IsRowOperator(IMethodSymbol method)
        => method.ContainingType is { } type
            && string.Equals(type.Name, SqlRowTypeName, StringComparison.Ordinal)
            && string.Equals(type.ContainingNamespace?.ToDisplayString(), LinqNamespace, StringComparison.Ordinal);

    // Sql.Row(t.A, t.B) > Sql.Row(p, q): ("A", "B") > (@p, @q). Both sides are Sql.Row calls;
    // SqlRow's operators take two rows of the same type, so they have the same arity. Each pair
    // is a column and a parameter or a non-null literal, in either order. Anything else is
    // ZAO102 at the comparison. A column stored by name is checked when the query is bound.
    private static SqlExpression? ReadRowComparison(
        IBinaryOperation binary,
        SqlBinaryOperator compare,
        IParameterSymbol? row,
        ReaderContext context)
    {
        if (RowItems(binary.LeftOperand) is not { } leftItems
            || RowItems(binary.RightOperand) is not { } rightItems
            || leftItems.Length != rightItems.Length)
        {
            return ReportUnsupported(binary, context);
        }

        var left = ImmutableArray.CreateBuilder<SqlExpression>(leftItems.Length);
        var right = ImmutableArray.CreateBuilder<SqlExpression>(rightItems.Length);
        for (var i = 0; i < leftItems.Length; i++)
        {
            // NULL in a row comparison makes it NULL, and an enum stored by its name would order the names.
            if (IsNullConstant(leftItems[i]) || IsNullConstant(rightItems[i])
                || IsNameStoredEnum(leftItems[i]) || IsNameStoredEnum(rightItems[i]))
            {
                return ReportUnsupported(binary, context);
            }

            var leftItem = ReadComparand(leftItems[i], row, context);
            if (leftItem is null) return null;
            var rightItem = ReadComparand(rightItems[i], row, context);
            if (rightItem is null) return null;
            if (!(leftItem is SqlMemberExpression && IsKeyValue(rightItem))
                && !(rightItem is SqlMemberExpression && IsKeyValue(leftItem)))
            {
                return ReportUnsupported(binary, context);
            }
            left.Add(leftItem);
            right.Add(rightItem);
        }

        return new SqlRowComparisonExpression(
            compare,
            new EquatableArray<SqlExpression>(left.MoveToImmutable()),
            new EquatableArray<SqlExpression>(right.MoveToImmutable()));
    }

    // The arguments of a Sql.Row call, by parameter position; null for any other row value.
    private static ImmutableArray<IOperation>? RowItems(IOperation operand)
    {
        if (Unwrap(operand) is not IInvocationOperation call || !IsLinqMember(call.TargetMethod, SqlTypeName, "Row"))
            return null;
        var items = ImmutableArray.CreateBuilder<IOperation>(call.Arguments.Length);
        for (var i = 0; i < call.Arguments.Length; i++)
            items.Add(ArgumentFor(call, i));
        return items.MoveToImmutable();
    }

    private static bool IsKeyValue(SqlExpression item)
        => item is SqlParameterExpression or SqlLiteralExpression or SqlEnumLiteralExpression;
}
