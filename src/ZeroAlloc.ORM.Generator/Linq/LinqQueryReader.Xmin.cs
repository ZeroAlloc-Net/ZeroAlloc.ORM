using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

// Optimistic concurrency through PostgreSQL's xmin, the id of the transaction that wrote the
// row's current version. Sql.Xmin(r) names it; so does a uint property the map maps to xmin,
// which the reader sees only as a uint member and the binder recognises by its column.
//
// xmin is a transaction id, compared only for equality, and PostgreSQL sets it itself. So it is
// read in two places only:
//   - a projection, into a uint constructor parameter, read with GetFieldValue<uint>;
//   - an == or != test with a uint companion parameter, which binds as NpgsqlDbType.Xid, so it
//     meets xmin as an xid without a cast. Npgsql would send a bare uint as an oid.
// Anywhere else, an ordering, <, >, arithmetic, a setter, Sql.Xmin is ZAO102 here, and a mapped
// property ZAO102 when it is bound. A uint parameter compared with xmin is used nowhere else;
// one never compared with xmin keeps the [Query] path's ZAO041, as uint has no binding of its own.
internal static partial class LinqQueryReader
{
    private const string XminColumn = "xmin";

    private const string XminReason = "since xmin is a transaction id, compared only for equality,";

    // Whether the operation is a Sql.Xmin(...) call.
    private static bool IsXminCall(IOperation op)
        => op is IInvocationOperation call && IsLinqMember(call.TargetMethod, SqlTypeName, "Xmin");

    // Whether a binary operation has Sql.Xmin(...) among its operands, at any depth.
    private static bool ContainsXmin(IOperation op) => Unwrap(op) switch
    {
        var unwrapped when IsXminCall(unwrapped) => true,
        IBinaryOperation binary => ContainsXmin(binary.LeftOperand) || ContainsXmin(binary.RightOperand),
        _ => false,
    };

    // ZAO102 for xmin inside arithmetic, `Sql.Xmin(r) + 1`, in a setter or a condition.
    private static void ReportXminArithmetic(IBinaryOperation binary, ReaderContext context)
        => context.Report(LinqDiagnostics.Unsupported("'" + binary.Syntax + "', " + XminReason, binary.Syntax.GetLocation()));

    // Sql.Xmin(r) as a value, which ReadOperand reaches: the xmin of the row the argument names,
    // the lambda's row or a joined source. Allowed only while a projection is read; anywhere else
    // that reaches here it is ZAO102.
    private static OperandRead ReadXmin(IInvocationOperation call, IParameterSymbol? row, ReaderContext context, out SqlExpression? expression)
    {
        expression = null;
        if (!context.XminAllowed)
        {
            context.Report(LinqDiagnostics.Unsupported(
                "'" + call.Syntax + "' outside a projection or an == or != test with a uint parameter, " + XminReason,
                call.Syntax.GetLocation()));
            return OperandRead.Reported;
        }

        expression = XminOf(call, row, context);
        return expression is null ? OperandRead.Reported : OperandRead.Read;
    }

    // The xmin column of the row Sql.Xmin's argument names; null after ZAO102 for any other argument.
    private static SqlSystemColumnExpression? XminOf(IInvocationOperation call, IParameterSymbol? row, ReaderContext context)
    {
        var argument = Unwrap(ArgumentFor(call, 0));
        if (row is not null && ShapeAt(argument, row, context.Row) is { IsEntity: true } shape)
            return new SqlSystemColumnExpression(XminColumn, shape.Alias);

        context.Report(LinqDiagnostics.Unsupported(
            "'" + call.Syntax + "', the xmin of something other than a row of the query,",
            call.Syntax.GetLocation()));
        return null;
    }

    // `Sql.Xmin(r) == expected` or `!=`: the xmin against a uint companion parameter, which then
    // binds as xid. Any other comparison, or any other value, is ZAO102.
    private static SqlBinaryExpression? ReadXminComparison(
        IBinaryOperation binary,
        SqlBinaryOperator compare,
        IParameterSymbol? row,
        ReaderContext context)
    {
        if (compare is not (SqlBinaryOperator.Equal or SqlBinaryOperator.NotEqual))
        {
            context.Report(LinqDiagnostics.Unsupported("'" + binary.Syntax + "', " + XminReason, binary.Syntax.GetLocation()));
            return null;
        }

        var leftIsXmin = IsXminCall(Unwrap(binary.LeftOperand));
        var xminCall = (IInvocationOperation)Unwrap(leftIsXmin ? binary.LeftOperand : binary.RightOperand);
        var other = Unwrap(leftIsXmin ? binary.RightOperand : binary.LeftOperand);
        if (other is not IParameterReferenceOperation { Parameter: var parameter } reference
            || !context.SqlNames.TryGetValue(parameter, out var sqlName)
            || parameter.Type.SpecialType != SpecialType.System_UInt32)
        {
            context.Report(LinqDiagnostics.Unsupported(
                "'" + binary.Syntax + "', comparing xmin with something other than a uint parameter,",
                binary.Syntax.GetLocation()));
            return null;
        }

        var xmin = XminOf(xminCall, row, context);
        if (xmin is null) return null;
        context.XidParameters.Add(parameter.Name);
        var value = new SqlParameterExpression(sqlName, parameter.Name, ColumnPicksEnumStorage: false, LocationInfo.From(reference.Syntax.GetLocation()));
        return leftIsXmin
            ? new SqlBinaryExpression(compare, xmin, value)
            : new SqlBinaryExpression(compare, value, xmin);
    }

    // A uint row member compared with a uint companion parameter, as `t.RowVersion == rowVersion`
    // reads for a property the map maps to xmin: the parameter binds as xid, and this use is
    // not one of its other uses. The binder checks the member is xmin, and reports any but an
    // == or != test there.
    private static void RecordMappedXminComparison(IBinaryOperation binary, SqlExpression left, SqlExpression right, ReaderContext context)
    {
        if (!IsUintMember(binary.LeftOperand, left) && !IsUintMember(binary.RightOperand, right)) return;
        foreach (var side in new[] { left, right })
        {
            if (side is SqlParameterExpression parameter && context.UintParameterUses.Remove(parameter))
                context.XidParameters.Add(parameter.ClrName);
        }
    }

    private static bool IsUintMember(IOperation operand, SqlExpression read)
        => read is SqlMemberExpression && Unwrap(operand).Type?.SpecialType == SpecialType.System_UInt32;

    // ZAO102 for each use of a uint parameter compared with xmin outside such a comparison: it
    // binds as an xid there, which any other use would meet as the wrong type.
    private static bool CheckXidParameters(ReaderContext context)
    {
        var valid = true;
        foreach (var use in context.UintParameterUses)
        {
            if (!context.XidParameters.Contains(use.ClrName)) continue;
            context.Report(LinqDiagnostics.UnsupportedAt(
                "Parameter '" + use.ClrName + "', a uint that binds as an xmin transaction id, used other than in a comparison with xmin,",
                use.Location));
            valid = false;
        }
        return valid;
    }
}
