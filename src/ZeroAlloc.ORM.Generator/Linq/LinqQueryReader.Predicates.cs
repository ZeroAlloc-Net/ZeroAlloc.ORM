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

    // WhereIf(condition, t => predicate) — ((condition) IS NOT TRUE OR (predicate)). The condition
    // is read without a row parameter: it tests companion parameters and constants only.
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
            case IPropertyReferenceOperation when HasValueOf(op, context) is { } tested:
                return ReadNullTestOf(tested, negated: true, op, row, context);
            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not, OperatorMethod: null } not
                when HasValueOf(Unwrap(not.Operand), context) is { } tested:
                return ReadNullTestOf(tested, negated: false, op, row, context);
            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not, OperatorMethod: null } not:
                var operand = ReadPredicate(not.Operand, row, context);
                return operand is null ? null : Negate(operand, context);
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

    // A comparison, with C#'s null semantics; see LinqQueryReader.Nulls.cs. `x == null` and
    // `x != null` become IS [NOT] NULL. A user-defined operator,
    // as on string, Guid, DateTimeOffset or a typed id, is accepted when it compares its own
    // type: the column and the value then compare by the primitive both are stored as.
    private static SqlExpression? ReadComparison(
        IBinaryOperation binary,
        SqlBinaryOperator compare,
        IParameterSymbol? row,
        ReaderContext context)
    {
        if (IsNullConstant(binary.LeftOperand) || IsNullConstant(binary.RightOperand))
        {
            if (compare is not (SqlBinaryOperator.Equal or SqlBinaryOperator.NotEqual))
                return ReportUnsupported(binary, context);
            return ReadNullTest(binary.LeftOperand, binary.RightOperand, compare == SqlBinaryOperator.NotEqual, binary, row, context);
        }

        if (binary.OperatorMethod is { } rowOperator && IsRowOperator(rowOperator))
            return ReadRowComparison(binary, compare, row, context);

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
        return Compare(compare, left, right, context);
    }

    // `x == null`, `null != x`, `string.Equals(x, null)` or `p.HasValue`: IS [NOT] NULL. Exactly one side must be
    // the null constant. A parameter without a DbType facet gets a cast here, since PostgreSQL
    // types a parameter at its first use and `@p IS NULL` gives it none; a type with no cast is
    // ZAO102 rather than a 42P08 at run time.
    private static SqlExpression? ReadNullTest(
        IOperation left,
        IOperation right,
        bool negated,
        IOperation at,
        IParameterSymbol? row,
        ReaderContext context)
    {
        var leftNull = IsNullConstant(left);
        if (leftNull && IsNullConstant(right)) return ReportUnsupported(at, context);
        return ReadNullTestOf(leftNull ? right : left, negated, at, row, context);
    }

    // `p.HasValue` on a nullable companion parameter: the parameter reference, which a null
    // test reads; null for anything else.
    private static IOperation? HasValueOf(IOperation op, ReaderContext context)
        => op is IPropertyReferenceOperation { Property.Name: "HasValue", Instance: IParameterReferenceOperation reference }
            && reference.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
            && context.SqlNames.ContainsKey(reference.Parameter)
                ? reference
                : null;

    // `tested IS [NOT] NULL`, reported at `at`.
    private static SqlIsNullExpression? ReadNullTestOf(
        IOperation testedOperation,
        bool negated,
        IOperation at,
        IParameterSymbol? row,
        ReaderContext context)
    {
        var tested = ReadComparand(testedOperation, row, context);
        if (tested is null) return null;

        string? cast = null;
        if (Unwrap(testedOperation) is IParameterReferenceOperation { Parameter: var parameter }
            && !TryCastUntyped(parameter, "'" + at.Syntax + "', a null test on", at, context, out cast))
        {
            return null;
        }
        return new SqlIsNullExpression(tested, negated, cast);
    }

    // The cast a parameter without a DbType facet needs where a null must have a type: null for
    // a typed parameter, else its NullTestCast. A type with no cast reports ZAO102 at `at`,
    // describing the use as `use '<name>'`, and returns false.
    private static bool TryCastUntyped(IParameterSymbol parameter, string use, IOperation at, ReaderContext context, out string? cast)
    {
        cast = null;
        if (context.TypedParameters.Contains(parameter.Name)) return true;
        cast = NullTestCast(parameter.Type, context.Conventions);
        if (cast is not null) return true;
        context.Report(LinqDiagnostics.Unsupported(
            use + " '" + parameter.Name + "', whose type '"
                + parameter.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString()
                + "' binds without a PostgreSQL type,",
            at.Syntax.GetLocation()));
        return false;
    }

    // The PostgreSQL type Npgsql infers for a parameter that has no DbType facet, for the casts
    // ReadNullTest writes. Null for a type outside this list. A short has the Int16 facet, so it
    // reaches here only as a value object's underlying type; char has no binding at all, ZAO041.
    private static string? NullTestCast(ITypeSymbol type, ConventionContext conventions)
    {
        var underlying = UnwrapNullable(type);
        if (ConventionDiscovery.Resolve(underlying, conventions).Kind == ConventionKind.PrimitiveArray)
        {
            var element = underlying switch
            {
                IArrayTypeSymbol array => array.ElementType,
                INamedTypeSymbol { TypeArguments.Length: 1 } named => named.TypeArguments[0],
                _ => null,
            };
            return element switch
            {
                { SpecialType: SpecialType.System_String } => "text[]",
                { SpecialType: SpecialType.System_Int32 } => "integer[]",
                { SpecialType: SpecialType.System_Int64 } => "bigint[]",
                { MetadataName: "Guid" } => "uuid[]",
                _ => null,
            };
        }
        return OrmGenerator.BoundReader(type, conventions) switch
        {
            "GetDateTime" => "timestamptz",
            "GetFieldValue<global::System.DateOnly>" => "date",
            "GetFieldValue<global::System.TimeOnly>" => "time",
            "GetFieldValue<global::System.TimeSpan>" => "interval",
            "GetByte" or "GetInt16" => "smallint",
            _ => null,
        };
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
            if (IsNullConstant(ArgumentFor(invocation, 0)) || IsNullConstant(ArgumentFor(invocation, 1)))
                return ReportUnsupported(invocation, context);
            var operand = ReadComparand(ArgumentFor(invocation, 0), row, context);
            if (operand is null) return null;
            var pattern = ReadComparand(ArgumentFor(invocation, 1), row, context);
            return pattern is null ? null : new SqlILikeExpression(operand, pattern);
        }

        if (IsStringEquals(invocation))
        {
            if (IsNullConstant(ArgumentFor(invocation, 0)) || IsNullConstant(ArgumentFor(invocation, 1)))
                return ReadNullTest(ArgumentFor(invocation, 0), ArgumentFor(invocation, 1), negated: false, invocation, row, context);
            var left = ReadComparand(ArgumentFor(invocation, 0), row, context);
            if (left is null) return null;
            var right = ReadComparand(ArgumentFor(invocation, 1), row, context);
            return right is null ? null : Compare(SqlBinaryOperator.Equal, left, right, context);
        }

        if (LikeMatch(invocation) is { } match && invocation.Instance is { } instance)
        {
            // A null pattern matches nothing in SQL, and throws in C#: never what was meant.
            if (IsNullConstant(invocation.Arguments[0].Value)) return ReportUnsupported(invocation, context);
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
    // that binds as a PostgreSQL array: T[], List<T> or IReadOnlyList<T> of string, int, long, float, double or Guid.
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
