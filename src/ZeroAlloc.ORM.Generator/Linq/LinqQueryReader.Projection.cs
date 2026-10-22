using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

internal static partial class LinqQueryReader
{
    // Select(t => new Row(...)) — the SELECT list. The row is a positional record built with the
    // constructor the FlatRow materializer reads, and FlatRow reads by position, so each argument
    // goes to the slot of the parameter it binds, whatever order named arguments are written in.
    private static bool ReadSelect(IInvocationOperation invocation, ChainBuilder chain, ReaderContext context)
    {
        if (invocation.Arguments.Length != 2
            || !TryReadLambda(invocation.Arguments[1].Value, out var row, out var returned))
        {
            return Unsupported(invocation, "'Select' without a lambda", context);
        }

        if (chain.IsSubquery) return ReadSubquerySelect(invocation, returned, row, chain, context);

        if (Unwrap(returned) is not IObjectCreationOperation creation)
            return RejectProjection("Select must construct a positional record with 'new'", returned, context);

        var expectedRow = invocation.TargetMethod.TypeArguments[1];
        if (creation.Type is not INamedTypeSymbol { IsRecord: true } record
            || !SymbolEqualityComparer.Default.Equals(record, expectedRow))
        {
            return RejectProjection("'" + expectedRow.ToDisplayString() + "' is not a positional record", creation, context);
        }
        if (creation.Constructor is not { } constructor
            || !SymbolEqualityComparer.Default.Equals(constructor, OrmGenerator.FlatRowConstructor(record)))
        {
            return RejectProjection(
                "construct '" + record.Name + "' with its widest public constructor, the one rows are read through",
                creation,
                context);
        }
        if (creation.Initializer is not null)
        {
            return RejectProjection(
                "an object initializer is not read; pass every value to the constructor", creation.Initializer, context);
        }

        var items = new SqlExpression[constructor.Parameters.Length];
        foreach (var argument in creation.Arguments)
        {
            if (argument.Parameter is not { } parameter || argument.ArgumentKind == ArgumentKind.ParamArray)
                return RejectProjection("a params argument is not supported", argument, context);
            var item = chain.Group is { } group
                ? ReadGroupedItem(argument.Value, row, group, context)
                : ReadProjectionItem(argument.Value, row, context);
            if (item is null || RejectsNull(item, argument.Value, parameter.Type, context)) return false;
            items[parameter.Ordinal] = item;
        }

        chain.Projection = ImmutableArray.Create(items);
        return true;
    }

    // A parameter that can be null and has no DbType facet is cast, so that a null still has a
    // type: PostgreSQL reads the SELECT list first and would type it as text there.
    private static SqlExpression? ReadProjectionItem(IOperation value, IParameterSymbol? row, ReaderContext context)
    {
        var op = Unwrap(value);
        if (NestedOwnedReason(op, row, context) is { } nested)
        {
            RejectProjection(nested, value, context);
            return null;
        }

        // A member whose type is an object: the map decides what it is when the query is bound,
        // an owned object, ZAO107, a composite column, ZAO102, or a plain column.
        if (WholeObjectMemberType(op, row, context) is { } whole
            && TryReadRowMember(op, row, context, out var name, out var type, out var alias))
        {
            var composite = IsComposite(whole, context.Conventions) ? whole.ToDisplayString() : null;
            var member = new SqlMemberExpression(
                name,
                LocationInfo.From(op.Syntax.GetLocation()),
                ColumnPicksEnumStorage(type),
                alias,
                new SqlWholeObject(op.Syntax.ToString(), whole.ToDisplayString(), composite));
            if (CanBeNull(type)) context.NullableValues.Add(member);
            return member;
        }

        switch (ReadOperand(value, row, context, out var expression))
        {
            case OperandRead.Read when Unwrap(value) is IParameterReferenceOperation { Parameter: var parameter }
                && CanBeNull(parameter.Type):
                if (!TryCastUntyped(parameter, "projecting", value, context, out var cast)) return null;
                return cast is null ? expression : new SqlCastExpression(expression!, cast);
            case OperandRead.Read:
                return expression;
            case OperandRead.Unsupported:
                RejectProjection(
                    "'" + value.Syntax + "' must be a mapped column, a companion parameter or a literal", value, context);
                return null;
            default:
                return null;
        }
    }

    // One rule for every projected value, a column, g.Key or an aggregate over one: a member of
    // an optional owned object, which reads NULL when the object is absent where C# would throw,
    // goes to a parameter that takes a null. ZAO107 when the parameter cannot take it. Only
    // values the owned branch lifted are checked; a plain nullable column keeps its old behaviour.
    private static bool RejectsNull(SqlExpression item, IOperation value, ITypeSymbol target, ReaderContext context)
    {
        if (CanBeNull(target) || !IsLifted(item, context.LiftedValues)) return false;

        var declared = Unwrap(value).Type ?? target;
        RejectProjection(
            "'" + value.Syntax + "' reads NULL when its optional owned object is absent; declare the parameter '"
                + declared.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString() + "?'",
            value,
            context);
        return true;
    }

    // A lifted member, or a Min, Max or Average over one, which is NULL when every value is.
    private static bool IsLifted(SqlExpression item, HashSet<SqlExpression> lifted) => item switch
    {
        SqlAggregateCallExpression { Function: not (SqlAggregateFunction.Count or SqlAggregateFunction.LongCount or SqlAggregateFunction.Sum), Argument: { } argument }
            => IsLifted(argument, lifted),
        SqlCastExpression cast => IsLifted(cast.Operand, lifted),
        SqlDateTruncExpression dateTrunc => IsLifted(dateTrunc.Operand, lifted),
        _ => lifted.Contains(item),
    };

    private static bool RejectProjection(string reason, IOperation at, ReaderContext context)
    {
        context.Report(LinqDiagnostics.UnsupportedProjection(reason, at.Syntax.GetLocation()));
        return false;
    }
}
