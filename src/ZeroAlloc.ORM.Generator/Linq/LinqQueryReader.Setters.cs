using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

internal static partial class LinqQueryReader
{
    private const string SetPropertyCallsTypeName = "SetPropertyCalls";

    // ExecuteUpdate(set => set.SetProperty(...).SetProperty(...)) — the SET list, in written
    // order. The setters lambda's body is a chain of SetProperty calls on its own parameter.
    private static bool ReadSetters(IInvocationOperation invocation, ChainBuilder chain, ReaderContext context)
    {
        if (invocation.Arguments.Length != 2
            || !TryReadLambda(invocation.Arguments[1].Value, out var calls, out var body))
        {
            return Unsupported(invocation, "'ExecuteUpdate' without a setters lambda", context);
        }

        if (!ReadSetterCalls(body, calls, chain, context)) return false;
        if (chain.Setters.Count == 0)
            return Unsupported(invocation, "'ExecuteUpdate' without a SetProperty", context);
        return true;
    }

    // The receiver first, so the setters keep the order they are written in.
    private static bool ReadSetterCalls(IOperation operation, IParameterSymbol calls, ChainBuilder chain, ReaderContext context)
    {
        var op = Unwrap(operation);
        if (op is IParameterReferenceOperation reference && IsParameter(reference, calls)) return true;
        if (op is IInvocationOperation { Instance: { } receiver } call
            && IsLinqMember(call.TargetMethod, SetPropertyCallsTypeName, "SetProperty"))
        {
            return ReadSetterCalls(receiver, calls, chain, context) && ReadSetter(call, chain, context);
        }

        context.Report(LinqDiagnostics.Unsupported(
            "'" + op.Syntax + "' in the setters, rather than SetProperty calls on '" + calls.Name + "',",
            op.Syntax.GetLocation()));
        return false;
    }

    // SetProperty(t => t.Col, value) or SetProperty(t => t.Col, t => computed).
    private static bool ReadSetter(IInvocationOperation call, ChainBuilder chain, ReaderContext context)
    {
        if (!TryReadLambda(ArgumentFor(call, 0), out var targetRow, out var targetBody))
            return Unsupported(call, "'SetProperty' without a column lambda", context);
        var target = ReadSetterTarget(Unwrap(targetBody), targetRow, context, out var json);
        if (target is null) return false;

        // The overload whose value is a lambda over the row, Func<T, TProperty>, rather than TProperty.
        SqlExpression? value;
        var valueArgument = ArgumentFor(call, 1);
        if (call.TargetMethod.OriginalDefinition.Parameters[1].Type.TypeKind == TypeKind.Delegate)
        {
            if (!TryReadLambda(valueArgument, out var valueRow, out var valueBody))
                return Unsupported(call, "'SetProperty' with a value that is not a lambda", context);
            value = ReadSetterValue(valueBody, valueRow, context);
        }
        else
        {
            value = ReadSetterValue(valueArgument, row: null, context);
        }
        if (value is null) return false;

        // A [Json] column binds its parameter as jsonb, as a [Json] parameter would.
        if (json && value is SqlParameterExpression parameter) context.JsonParameters.Add(parameter.ClrName);

        chain.Setters.Add(new SqlSetter(target, value));
        return true;
    }

    // The column a setter writes: a member of the row, which the map resolves when the statement
    // is bound, an owned member included. A member whose type is an object goes there whole, for
    // the map to decide: one plain column, or an owned object or a composite, ZAO102. `json` is
    // whether the member is a string marked [Json], a jsonb document.
    private static SqlMemberExpression? ReadSetterTarget(IOperation op, IParameterSymbol row, ReaderContext context, out bool json)
    {
        json = false;
        if (!TryReadRowMember(op, row, context, out var name, out var type, out var alias))
        {
            context.Report(LinqDiagnostics.Unsupported(
                "'" + op.Syntax + "' as a setter target, rather than a mapped column of the row,",
                op.Syntax.GetLocation()));
            return null;
        }

        json = op is IPropertyReferenceOperation { Property: var property } && OrmGenerator.IsJsonString(property, property.Type);
        var whole = WholeObjectMemberType(op, row, context);
        return new SqlMemberExpression(
            name,
            LocationInfo.From(op.Syntax.GetLocation()),
            ColumnPicksEnumStorage(type),
            alias,
            whole is null
                ? null
                : new SqlWholeObject(op.Syntax.ToString(), whole.ToDisplayString(), IsComposite(whole, context.Conventions) ? whole.ToDisplayString() : null));
    }
}
