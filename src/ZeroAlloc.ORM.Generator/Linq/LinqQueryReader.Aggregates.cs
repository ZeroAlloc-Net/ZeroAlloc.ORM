using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.ORM.Generator.Linq;

// Sum, Min, Max and Average terminals over a column. The call renders so its value equals LINQ
// to Objects', in exactly the CLR type the [Query] scalar shape reads:
//
//   Sum      COALESCE(sum(x), 0), cast to the overload's type: LINQ's Sum is 0 for no values,
//            and sum(int) is bigint, sum(bigint) numeric. A decimal column needs no cast.
//   Average  avg(x)::double precision for a double result, avg(x) for a decimal one.
//   Min/Max  min(x) and max(x), which keep the column's type; cast only after a widening cast.
//
// The selector is a mapped column, optionally cast to a wider or nullable type, such as
// (long)e.InputTokens or (double?)e.OutputTokens. The cast picks the overload and so the
// result cast. The column itself is cast only for a Sum over real, see SumArgument.
internal static partial class LinqQueryReader
{
    private static bool IsValueAggregate(string operatorName)
        => operatorName is "Sum" or "Min" or "Max" or "Average";

    private static bool ReadAggregate(IInvocationOperation invocation, ChainBuilder chain, ReaderContext context)
    {
        var name = invocation.TargetMethod.Name;
        if (invocation.Arguments.Length != 2
            || !TryReadLambda(invocation.Arguments[1].Value, out var row, out var returned))
        {
            return Unsupported(invocation, "'" + name + "' without a lambda", context);
        }

        var argument = ReadAggregatedColumn(name, returned, row, context);
        if (argument is null) return false;

        var function = AggregateFunctionOf(name);
        var column = Unwrap(PeelWidening(returned, context)).Type!;
        var value = ((INamedTypeSymbol)invocation.TargetMethod.Parameters[1].Type).TypeArguments[1];
        if (function is SqlAggregateFunction.Min or SqlAggregateFunction.Max
            && UnwrapNullable(value).SpecialType == SpecialType.System_Boolean)
        {
            context.Report(LinqDiagnostics.Unsupported(
                "'" + name + "' over the bool '" + returned.Syntax + "', which PostgreSQL has no min or max for,",
                returned.Syntax.GetLocation()));
            return false;
        }

        var result = ((INamedTypeSymbol)invocation.TargetMethod.ReturnType).TypeArguments[0];
        chain.Call = new SqlAggregateCallExpression(
            function,
            SumArgument(function, argument, value, column),
            AggregateCast(function, value, column, result),
            CoalesceToZero: function == SqlAggregateFunction.Sum);
        chain.TerminalOperator = name;
        return true;
    }

    // The one case where the column is cast: a Sum over a real column, widened to double.
    // PostgreSQL's sum(real) adds in float4 and loses precision before any result cast, where
    // LINQ adds the widened doubles; sum("X"::double precision) adds them as LINQ does.
    private static SqlExpression SumArgument(
        SqlAggregateFunction function,
        SqlMemberExpression column,
        ITypeSymbol value,
        ITypeSymbol columnType)
        => function == SqlAggregateFunction.Sum
            && UnwrapNullable(columnType).SpecialType == SpecialType.System_Single
            && UnwrapNullable(value).SpecialType == SpecialType.System_Double
                ? new SqlCastExpression(column, "double precision")
                : column;

    // The column the selector reads, through any cast PeelWidening and Unwrap peel. A
    // parameter, a literal or a computed value is ZAO102.
    private static SqlMemberExpression? ReadAggregatedColumn(
        string operatorName,
        IOperation returned,
        IParameterSymbol row,
        ReaderContext context)
    {
        var read = ReadOperand(PeelWidening(returned, context), row, context, out var expression);
        if (read == OperandRead.Reported) return null;
        if (read == OperandRead.Read && expression is SqlMemberExpression member) return member;

        context.Report(LinqDiagnostics.Unsupported(
            "'" + returned.Syntax + "' as the value of '" + operatorName
                + "', rather than a mapped column or one cast to a wider type,",
            returned.Syntax.GetLocation()));
        return null;
    }

    // Peels the casts a selector may write around its column: those that convert implicitly,
    // whatever the cast says. Roslyn classifies (DateTime?)e.ExecutedAt as an explicit nullable
    // conversion, which Unwrap keeps, though DateTime converts to DateTime? implicitly. A
    // narrowing cast, such as (int)e.Size, stays, and so is no column.
    private static IOperation PeelWidening(IOperation operation, ReaderContext context)
    {
        var compilation = context.Model.Compilation;
        while (operation is IConversionOperation { Operand.Type: { } from, Type: { } to } conversion
            && !conversion.Conversion.IsUserDefined
            && compilation.ClassifyCommonConversion(from, to) is { IsImplicit: true, IsUserDefined: false })
        {
            operation = conversion.Operand;
        }
        return operation;
    }

    private static SqlAggregateFunction AggregateFunctionOf(string operatorName) => operatorName switch
    {
        "Sum" => SqlAggregateFunction.Sum,
        "Min" => SqlAggregateFunction.Min,
        "Max" => SqlAggregateFunction.Max,
        _ => SqlAggregateFunction.Average,
    };

    // The cast that makes PostgreSQL's result type the CLR type the scalar reads; null for none.
    // `value` is the selector's type, `column` the column's and `result` the terminal's.
    private static string? AggregateCast(
        SqlAggregateFunction function,
        ITypeSymbol value,
        ITypeSymbol column,
        ITypeSymbol result)
    {
        var valueType = UnwrapNullable(value);
        var sameAsColumn = SymbolEqualityComparer.Default.Equals(valueType, UnwrapNullable(column));
        return function switch
        {
            SqlAggregateFunction.Sum when valueType.SpecialType == SpecialType.System_Decimal && sameAsColumn => null,
            SqlAggregateFunction.Sum => PostgresNumberType(valueType.SpecialType),
            SqlAggregateFunction.Average => UnwrapNullable(result).SpecialType == SpecialType.System_Double ? "double precision" : null,
            _ => sameAsColumn ? null : PostgresNumberType(valueType.SpecialType),
        };
    }

    private static string? PostgresNumberType(SpecialType type) => type switch
    {
        SpecialType.System_Int32 => "int",
        SpecialType.System_Int64 => "bigint",
        SpecialType.System_Double => "double precision",
        SpecialType.System_Decimal => "numeric",
        _ => null,
    };
}
