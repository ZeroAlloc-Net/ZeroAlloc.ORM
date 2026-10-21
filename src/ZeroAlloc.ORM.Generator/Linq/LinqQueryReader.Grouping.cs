using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.ORM.Generator.Linq;

// GroupBy(t => key) and what reads its groups. The key renders as GROUP BY; the operators after
// it take a group `g`, of which they read g.Key, a member g.Key.Name of an anonymous key, and the
// group aggregates g.Count(), g.LongCount(), g.Sum, g.Min, g.Max and g.Average. A key is:
//
//   a mapped column                     GroupBy(t => t.Status)
//   a DateTime or DateTimeOffset        GroupBy(t => t.CreatedAt.Date), date_trunc('day', ...)
//   column's .Date
//   an anonymous type of those          GroupBy(t => new { t.Note, Day = t.CreatedAt.Date })
//
// A group always has rows, so a group aggregate needs no COALESCE, with one exception: a Sum
// over a nullable selector, whose values may all be null, is 0 in LINQ.
internal static partial class LinqQueryReader
{
    private const string GroupOperatorsTypeName = "GroupOperators";

    /// <summary>
    /// The GroupBy key: the GROUP BY expressions, and for an anonymous key each member's
    /// expression by name. A single key has no members; g.Key is its one expression.
    /// </summary>
    private sealed class GroupKey(ImmutableArray<SqlExpression> keys, Dictionary<string, SqlExpression>? members)
    {
        public ImmutableArray<SqlExpression> Keys { get; } = keys;

        public Dictionary<string, SqlExpression>? Members { get; } = members;
    }

    // GroupBy(t => key). It reads the rows the WHERE leaves; an ordering or a page before it
    // would apply to the rows, not the groups, which one SELECT cannot express.
    private static bool ReadGroupBy(IInvocationOperation invocation, ChainBuilder chain, ReaderContext context)
    {
        if (chain.Group is not null) return Unsupported(invocation, "'GroupBy' after 'GroupBy'", context);
        if (chain.Ordering.Count > 0) return Unsupported(invocation, "'GroupBy' after 'OrderBy'", context);
        if (invocation.Arguments.Length != 2
            || !TryReadLambda(invocation.Arguments[1].Value, out var row, out var returned))
        {
            return Unsupported(invocation, "'GroupBy' without a lambda", context);
        }

        var keys = ImmutableArray.CreateBuilder<SqlExpression>();
        if (Unwrap(returned) is IAnonymousObjectCreationOperation anonymous)
        {
            var members = new Dictionary<string, SqlExpression>(StringComparer.Ordinal);
            foreach (var initializer in anonymous.Initializers)
            {
                if (initializer is not ISimpleAssignmentOperation { Target: IPropertyReferenceOperation property, Value: var value })
                    return ReportGroupKey(initializer, context);
                var key = ReadGroupKeyValue(value, initializer, row, context);
                if (key is null) return false;
                keys.Add(key);
                members[property.Property.Name] = key;
            }
            chain.Group = new GroupKey(keys.ToImmutable(), members);
            return true;
        }

        var single = ReadGroupKeyValue(returned, returned, row, context);
        if (single is null) return false;
        keys.Add(single);
        chain.Group = new GroupKey(keys.ToImmutable(), members: null);
        return true;
    }

    // A column, or a column's .Date. `at` is where a wrong key is reported: the key, or the
    // anonymous member that holds it.
    private static SqlExpression? ReadGroupKeyValue(IOperation value, IOperation at, IParameterSymbol row, ReaderContext context)
    {
        var op = Unwrap(value);
        var truncated = false;
        if (op is IPropertyReferenceOperation { Property.Name: "Date", Instance: { } instance } date
            && IsDateTimeOrOffset(date.Property.ContainingType))
        {
            op = Unwrap(instance);
            truncated = true;
        }

        var read = ReadOperand(op, row, context, out var expression);
        if (read == OperandRead.Reported) return null;
        if (read != OperandRead.Read || expression is not SqlMemberExpression column)
        {
            ReportGroupKey(at, context);
            return null;
        }
        return truncated ? new SqlDateTruncExpression(column) : column;
    }

    private static bool ReportGroupKey(IOperation at, ReaderContext context)
    {
        context.Report(LinqDiagnostics.Unsupported(
            "'" + at.Syntax + "' as a GroupBy key, rather than a mapped column, its .Date or an anonymous type of those,",
            at.Syntax.GetLocation()));
        return false;
    }

    private static bool IsDateTimeOrOffset(ITypeSymbol type)
        => type.SpecialType == SpecialType.System_DateTime || IsDateTimeOffset(type);

    // A projection argument over a group: a group value, or a parameter or a literal as in any
    // projection. Anything else that reads the group, such as g.Count() + 1, is ZAO102: only
    // the keys and the aggregates are one value per group.
    private static SqlExpression? ReadGroupedItem(IOperation value, IParameterSymbol group, GroupKey key, ReaderContext context)
    {
        switch (ReadGroupValue(value, group, key, context, out var expression))
        {
            case OperandRead.Read:
                return expression;
            case OperandRead.Reported:
                return null;
        }
        if (ReadsGroup(value, group))
        {
            ReportNotGrouped(value, context);
            return null;
        }
        return ReadProjectionItem(value, row: null, context);
    }

    // g.Key, g.Key.Member, or a group aggregate. Unsupported for any other form, which the
    // caller reports in its own terms.
    private static OperandRead ReadGroupValue(
        IOperation value,
        IParameterSymbol group,
        GroupKey key,
        ReaderContext context,
        out SqlExpression? expression)
    {
        expression = null;
        var op = Unwrap(value);
        if (IsGroupKey(op, group))
        {
            if (key.Members is not null)
            {
                context.Report(LinqDiagnostics.Unsupported(
                    "'" + op.Syntax + "', an anonymous key read whole rather than by its members,", op.Syntax.GetLocation()));
                return OperandRead.Reported;
            }
            expression = key.Keys[0];
            return OperandRead.Read;
        }
        if (op is IPropertyReferenceOperation { Instance: { } owner } member
            && IsGroupKey(owner, group)
            && key.Members is { } members
            && members.TryGetValue(member.Property.Name, out var memberKey))
        {
            expression = memberKey;
            return OperandRead.Read;
        }
        if (op is IInvocationOperation invocation && IsLinqType(invocation.TargetMethod, GroupOperatorsTypeName))
        {
            expression = ReadGroupAggregate(invocation, group, context);
            return expression is null ? OperandRead.Reported : OperandRead.Read;
        }
        return OperandRead.Unsupported;
    }

    private static bool IsGroupKey(IOperation op, IParameterSymbol group)
        => op is IPropertyReferenceOperation { Property.Name: "Key", Instance: IParameterReferenceOperation owner }
            && IsParameter(owner, group);

    // Whether the value reads the group anywhere, as g.Count() + 1 or g.Key.Day.Year do.
    private static bool ReadsGroup(IOperation value, IParameterSymbol group)
        => (value is IParameterReferenceOperation self && IsParameter(self, group))
            || value.Descendants().Any(d => d is IParameterReferenceOperation reference && IsParameter(reference, group));

    private static void ReportNotGrouped(IOperation value, ReaderContext context)
        => context.Report(LinqDiagnostics.Unsupported(
            "'" + value.Syntax + "' over a group, rather than g.Key, a key member or a group aggregate,",
            value.Syntax.GetLocation()));

    // g.Count(), g.LongCount(), g.Sum, g.Min, g.Max or g.Average over the group's rows. Sum,
    // Min, Max and Average render as their scalar forms do, with the same casts, but without
    // COALESCE, except a Sum over a nullable selector.
    private static SqlAggregateCallExpression? ReadGroupAggregate(
        IInvocationOperation invocation,
        IParameterSymbol group,
        ReaderContext context)
    {
        var name = invocation.TargetMethod.Name;
        if (invocation.Arguments.Length == 0
            || Unwrap(invocation.Arguments[0].Value) is not IParameterReferenceOperation source
            || !IsParameter(source, group))
        {
            ReportNotGrouped(invocation, context);
            return null;
        }
        if (string.Equals(name, "Count", StringComparison.Ordinal))
            return new SqlAggregateCallExpression(SqlAggregateFunction.Count, null, "int", CoalesceToZero: false);
        if (string.Equals(name, "LongCount", StringComparison.Ordinal))
            return new SqlAggregateCallExpression(SqlAggregateFunction.LongCount, null, null, CoalesceToZero: false);
        if (!IsValueAggregate(name)
            || invocation.Arguments.Length != 2
            || !TryReadLambda(invocation.Arguments[1].Value, out var row, out var returned))
        {
            ReportNotGrouped(invocation, context);
            return null;
        }

        var argument = ReadAggregatedColumn(name, returned, row, context);
        if (argument is null) return null;

        var function = AggregateFunctionOf(name);
        var column = Unwrap(PeelWidening(returned, context)).Type!;
        var value = ((INamedTypeSymbol)invocation.TargetMethod.Parameters[1].Type).TypeArguments[1];
        if (function is SqlAggregateFunction.Min or SqlAggregateFunction.Max && !HasGroupMinMax(value))
        {
            context.Report(LinqDiagnostics.Unsupported(
                "'" + name + "' over '" + returned.Syntax + "', a bool, an enum or a Guid, which PostgreSQL has no "
                    + "min or max for or orders differently from C#,",
                returned.Syntax.GetLocation()));
            return null;
        }

        return new SqlAggregateCallExpression(
            function,
            SumArgument(function, argument, value, column),
            AggregateCast(function, value, column, invocation.TargetMethod.ReturnType),
            CoalesceToZero: function == SqlAggregateFunction.Sum && CanBeNull(value));
    }

    // PostgreSQL has no min or max over boolean or uuid, and an enum stored by name would order
    // its names, not its values as C# does.
    private static bool HasGroupMinMax(ITypeSymbol value)
    {
        var type = UnwrapNullable(value);
        return type.SpecialType != SpecialType.System_Boolean
            && type.TypeKind != TypeKind.Enum
            && !string.Equals(type.ToDisplayString(), "System.Guid", StringComparison.Ordinal);
    }
}
