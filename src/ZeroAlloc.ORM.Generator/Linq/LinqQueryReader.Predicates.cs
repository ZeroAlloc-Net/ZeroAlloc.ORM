using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using ZeroAlloc.TypeConversions;

namespace ZeroAlloc.ORM.Generator.Linq;

internal static partial class LinqQueryReader
{
    // Where(t => predicate) — a WHERE condition. A top-level && splits into separate conditions,
    // which the renderer joins with AND, so Where(a && b) reads as Where(a).Where(b).
    private static bool ReadWhere(IInvocationOperation invocation, ChainBuilder chain, ReaderContext context)
    {
        if (invocation.Arguments.Length != 2
            || !TryReadLambda(invocation.Arguments[1].Value, out var row, out var body))
        {
            return Unsupported(invocation, "'Where' without a lambda", context);
        }

        var predicate = ReadPredicate(body, row, context);
        if (predicate is null) return false;
        AddConjuncts(chain.Predicates, predicate);
        return true;
    }

    // WhereIf(condition, t => predicate) — (NOT (condition) OR (predicate)). The condition is
    // read without a row parameter: it tests companion parameters and constants only.
    private static bool ReadWhereIf(IInvocationOperation invocation, ChainBuilder chain, ReaderContext context)
    {
        if (invocation.Arguments.Length != 3
            || !TryReadLambda(invocation.Arguments[2].Value, out var row, out var body))
        {
            return Unsupported(invocation, "'WhereIf' without a lambda", context);
        }

        var condition = ReadPredicate(invocation.Arguments[1].Value, row: null, context);
        if (condition is null) return false;
        var predicate = ReadPredicate(body, row, context);
        if (predicate is null) return false;
        chain.Predicates.Add(new SqlConditionalExpression(condition, predicate));
        return true;
    }

    private static void AddConjuncts(ImmutableArray<SqlExpression>.Builder predicates, SqlExpression predicate)
    {
        if (predicate is SqlBinaryExpression { Operator: SqlBinaryOperator.And } and)
        {
            AddConjuncts(predicates, and.Left);
            AddConjuncts(predicates, and.Right);
            return;
        }
        predicates.Add(predicate);
    }

    // A boolean condition: a comparison, &&, ||, !, a supported call, or a bool value alone.
    // Returns null after reporting ZAO102 or ZAO103.
    private static SqlExpression? ReadPredicate(IOperation operation, IParameterSymbol? row, ReaderContext context)
    {
        var op = Unwrap(operation);
        switch (op)
        {
            case IBinaryOperation binary:
                return ReadBinary(binary, row, context);
            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not, OperatorMethod: null } not:
                var operand = ReadPredicate(not.Operand, row, context);
                return operand is null ? null : new SqlNotExpression(operand);
            case IInvocationOperation invocation:
                return ReadPredicateCall(invocation, row, context);
        }

        // A bool column, parameter or constant on its own.
        if (op.Type?.SpecialType == SpecialType.System_Boolean)
            return ReadComparand(op, row, context);
        return ReportUnsupported(op, context);
    }

    private static SqlExpression? ReadBinary(IBinaryOperation binary, IParameterSymbol? row, ReaderContext context)
    {
        if (binary.OperatorKind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr
            && binary.OperatorMethod is null)
        {
            var left = ReadPredicate(binary.LeftOperand, row, context);
            if (left is null) return null;
            var right = ReadPredicate(binary.RightOperand, row, context);
            if (right is null) return null;
            var logical = binary.OperatorKind == BinaryOperatorKind.ConditionalAnd ? SqlBinaryOperator.And : SqlBinaryOperator.Or;
            return new SqlBinaryExpression(logical, left, right);
        }

        var comparison = binary.OperatorKind switch
        {
            BinaryOperatorKind.Equals => SqlBinaryOperator.Equal,
            BinaryOperatorKind.NotEquals => SqlBinaryOperator.NotEqual,
            BinaryOperatorKind.LessThan => SqlBinaryOperator.LessThan,
            BinaryOperatorKind.LessThanOrEqual => SqlBinaryOperator.LessThanOrEqual,
            BinaryOperatorKind.GreaterThan => SqlBinaryOperator.GreaterThan,
            BinaryOperatorKind.GreaterThanOrEqual => SqlBinaryOperator.GreaterThanOrEqual,
            _ => (SqlBinaryOperator?)null,
        };
        if (comparison is not { } compare) return ReportUnsupported(binary, context);
        return ReadComparison(binary, compare, row, context);
    }

    // A comparison. `x == null` and `x != null` become IS [NOT] NULL. A user-defined operator,
    // as on string, Guid, DateTimeOffset or a typed id, is accepted when it compares its own
    // type: the column and the value then compare by the primitive both are stored as.
    private static SqlExpression? ReadComparison(
        IBinaryOperation binary,
        SqlBinaryOperator compare,
        IParameterSymbol? row,
        ReaderContext context)
    {
        var leftNull = IsNullConstant(binary.LeftOperand);
        var rightNull = IsNullConstant(binary.RightOperand);
        if (leftNull || rightNull)
        {
            if ((leftNull && rightNull) || compare is not (SqlBinaryOperator.Equal or SqlBinaryOperator.NotEqual))
                return ReportUnsupported(binary, context);
            var tested = ReadComparand(leftNull ? binary.RightOperand : binary.LeftOperand, row, context);
            return tested is null ? null : new SqlIsNullExpression(tested, Negated: compare == SqlBinaryOperator.NotEqual);
        }

        if (binary.OperatorMethod is { } method && !ComparesOwnType(method, binary))
            return ReportUnsupported(binary, context);

        // A [StoreAsString] enum is stored as its name, so <, >, <= and >= would order the names,
        // not the values. A column the map stores by name is checked when the query is bound.
        if (compare is not (SqlBinaryOperator.Equal or SqlBinaryOperator.NotEqual)
            && (IsNameStoredEnum(binary.LeftOperand) || IsNameStoredEnum(binary.RightOperand)))
        {
            return ReportUnsupported(binary, context);
        }

        var left = ReadComparand(binary.LeftOperand, row, context);
        if (left is null) return null;
        var right = ReadComparand(binary.RightOperand, row, context);
        if (right is null) return null;
        return new SqlBinaryExpression(compare, left, right);
    }

    private static bool ComparesOwnType(IMethodSymbol method, IBinaryOperation binary)
        => OperandIs(binary.LeftOperand, method.ContainingType) && OperandIs(binary.RightOperand, method.ContainingType);

    private static bool OperandIs(IOperation operand, INamedTypeSymbol type)
        => Unwrap(operand).Type is { } operandType
            && SymbolEqualityComparer.Default.Equals(UnwrapNullable(operandType), type);

    private static bool IsNameStoredEnum(IOperation operand)
        => Unwrap(operand).Type is { } type
            && UnwrapNullable(type) is INamedTypeSymbol { TypeKind: TypeKind.Enum } named
            && StoresName(named);

    private static bool IsNullConstant(IOperation operand)
    {
        var op = Unwrap(operand);
        return op.ConstantValue.HasValue && op.ConstantValue.Value is null;
    }

    // Sql.ILike, string.Contains / StartsWith / EndsWith, and Contains on an array parameter.
    private static SqlExpression? ReadPredicateCall(IInvocationOperation invocation, IParameterSymbol? row, ReaderContext context)
    {
        var method = invocation.TargetMethod;
        if (IsLinqMember(method, SqlTypeName, "ILike") && invocation.Arguments.Length == 2)
        {
            var operand = ReadComparand(ArgumentFor(invocation, 0), row, context);
            if (operand is null) return null;
            var pattern = ReadComparand(ArgumentFor(invocation, 1), row, context);
            return pattern is null ? null : new SqlILikeExpression(operand, pattern);
        }

        if (IsStringEquals(invocation))
        {
            var left = ReadComparand(ArgumentFor(invocation, 0), row, context);
            if (left is null) return null;
            var right = ReadComparand(ArgumentFor(invocation, 1), row, context);
            return right is null ? null : new SqlBinaryExpression(SqlBinaryOperator.Equal, left, right);
        }

        if (LikeMatch(invocation) is { } match && invocation.Instance is { } instance)
        {
            var operand = ReadComparand(instance, row, context);
            if (operand is null) return null;
            var value = ReadComparand(invocation.Arguments[0].Value, row, context);
            return value is null ? null : new SqlLikeExpression(operand, value, match);
        }

        if (string.Equals(method.Name, "Contains", StringComparison.Ordinal) && CollectionContains(invocation) is { } membership)
            return ReadArrayContains(invocation, membership.Collection, membership.Item, row, context);

        return ReportUnsupported(invocation, context);
    }

    // string.Contains, StartsWith or EndsWith with a string argument, alone or followed by
    // StringComparison.Ordinal: LIKE matches characters exactly under PostgreSQL's
    // deterministic collations, which is what Ordinal means. Any other comparison is ZAO102.
    private static SqlLikeMatch? LikeMatch(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (method.IsStatic
            || method.ContainingType?.SpecialType != SpecialType.System_String
            || method.Parameters.Length is not (1 or 2)
            || method.Parameters[0].Type.SpecialType != SpecialType.System_String
            || (method.Parameters.Length == 2 && !IsOrdinal(invocation, 1)))
        {
            return null;
        }
        return method.Name switch
        {
            "Contains" => SqlLikeMatch.Contains,
            "StartsWith" => SqlLikeMatch.StartsWith,
            "EndsWith" => SqlLikeMatch.EndsWith,
            _ => null,
        };
    }

    // string.Equals(a, b) or string.Equals(a, b, StringComparison.Ordinal): `a = b`, the same
    // as `a == b`. The form analyzers such as MA0006 ask for, so a companion needs no suppression.
    private static bool IsStringEquals(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        return method.IsStatic
            && string.Equals(method.Name, "Equals", StringComparison.Ordinal)
            && method.ContainingType?.SpecialType == SpecialType.System_String
            && method.Parameters.Length is 2 or 3
            && method.Parameters[0].Type.SpecialType == SpecialType.System_String
            && method.Parameters[1].Type.SpecialType == SpecialType.System_String
            && (method.Parameters.Length == 2 || IsOrdinal(invocation, 2));
    }

    // Whether the argument at `ordinal` is the constant StringComparison.Ordinal.
    private static bool IsOrdinal(IInvocationOperation invocation, int ordinal)
    {
        var parameter = invocation.TargetMethod.Parameters[ordinal];
        var value = Unwrap(ArgumentFor(invocation, ordinal));
        return string.Equals(parameter.Type.ToDisplayString(), "System.StringComparison", StringComparison.Ordinal)
            && value.ConstantValue is { HasValue: true, Value: int comparison }
            && comparison == (int)StringComparison.Ordinal;
    }

    // The collection and the item of a membership test: `Enumerable.Contains(c, x)`, the span
    // `MemoryExtensions.Contains(c, x)` C# 14 binds an array to, or an instance `c.Contains(x)`
    // on a System.Collections.Generic type such as List<T>.
    private static (IOperation Collection, IOperation Item)? CollectionContains(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        var ns = method.ContainingType?.ContainingNamespace?.ToDisplayString();
        if (invocation.Instance is { } instance)
        {
            return invocation.Arguments.Length == 1 && string.Equals(ns, "System.Collections.Generic", StringComparison.Ordinal)
                ? (instance, invocation.Arguments[0].Value)
                : null;
        }

        var typeName = method.ContainingType?.Name;
        var known = (string.Equals(ns, "System.Linq", StringComparison.Ordinal) && string.Equals(typeName, "Enumerable", StringComparison.Ordinal))
            || (string.Equals(ns, "System", StringComparison.Ordinal) && string.Equals(typeName, "MemoryExtensions", StringComparison.Ordinal));
        return known && invocation.Arguments.Length == 2
            ? (ArgumentFor(invocation, 0), ArgumentFor(invocation, 1))
            : null;
    }

    // `ids.Contains(t.Id)` — `"Id" = ANY(@ids)`. The collection must be a companion parameter
    // that binds as a PostgreSQL array: T[], List<T> or IReadOnlyList<T> of string, int, long or Guid.
    private static SqlExpression? ReadArrayContains(
        IInvocationOperation invocation,
        IOperation collection,
        IOperation item,
        IParameterSymbol? row,
        ReaderContext context)
    {
        var source = Unwrap(collection);
        if (source is not IParameterReferenceOperation reference
            || !context.SqlNames.ContainsKey(reference.Parameter)
            || ConventionDiscovery.Resolve(reference.Parameter.Type, context.Conventions).Kind != ConventionKind.PrimitiveArray)
        {
            if (IsCapture(source, row))
            {
                context.Report(LinqDiagnostics.Capture(source.Syntax.ToString(), source.Syntax.GetLocation()));
                return null;
            }
            return ReportUnsupported(invocation, context);
        }

        if (ReadOperand(source, row, context, out var array) != OperandRead.Read) return ReportUnsupported(invocation, context);
        var operand = ReadComparand(item, row, context);
        return operand is null ? null : new SqlAnyExpression(operand, array!);
    }

    // The value an argument passes, by parameter position, whatever order named arguments take.
    private static IOperation ArgumentFor(IInvocationOperation invocation, int ordinal)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == ordinal) return argument.Value;
        }
        return invocation.Arguments[ordinal].Value;
    }

    // One side of a comparison or call: a column, a parameter or a constant.
    private static SqlExpression? ReadComparand(IOperation value, IParameterSymbol? row, ReaderContext context)
    {
        switch (ReadOperand(value, row, context, out var expression))
        {
            case OperandRead.Read:
                return expression;
            case OperandRead.Unsupported:
                return ReportUnsupported(Unwrap(value), context);
            default:
                return null;
        }
    }

    private static SqlExpression? ReportUnsupported(IOperation op, ReaderContext context)
    {
        context.Report(LinqDiagnostics.Unsupported("'" + op.Syntax + "'", op.Syntax.GetLocation()));
        return null;
    }
}
