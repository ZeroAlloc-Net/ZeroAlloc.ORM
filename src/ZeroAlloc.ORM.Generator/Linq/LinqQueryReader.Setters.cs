using System;
using System.Collections.Generic;
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
        return CheckJsonParameters(chain, context);
    }

    // A parameter set into a [Json] column binds as jsonb wherever the statement names it. Named
    // in a condition or in another setter too, it would meet a text or other column there as
    // jsonb, which PostgreSQL rejects at run time. The conditions are read already: they come
    // before the write.
    //
    // Conservative on purpose: a condition that compares the parameter with another [Json]
    // column, `t.Snapshot == doc`, would bind as jsonb consistently and work, yet is rejected
    // too. Telling it apart means knowing every column each use meets, nested subqueries and
    // WhereIf included, which a jsonb document compared for equality does not justify; the
    // predicate side is tracked as #402.
    private static bool CheckJsonParameters(ChainBuilder chain, ReaderContext context)
    {
        var elsewhere = SqlParameterReferences.Collect(chain.Predicates);
        for (var i = 0; i < chain.Setters.Count; i++)
        {
            if (!chain.JsonSetters.Contains(i)) elsewhere.UnionWith(SqlParameterReferences.Collect(new[] { chain.Setters[i].Value }));
        }

        var valid = true;
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var i in chain.JsonSetters)
        {
            var setter = chain.Setters[i];
            if (setter.Value is not SqlParameterExpression parameter
                || !elsewhere.Contains(parameter.Name)
                || !reported.Add(parameter.Name))
            {
                continue;
            }
            context.Report(LinqDiagnostics.UnsupportedAt(
                "Parameter '" + parameter.ClrName + "', which binds as jsonb for the [Json] '" + setter.Column.Member
                    + "', used in a condition or another setter as well,",
                parameter.Location));
            valid = false;
        }
        return valid;
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
        var member = Unwrap(targetBody);
        var target = ReadSetterTarget(member, targetRow, context, out var json);
        if (target is null) return false;

        // C# infers TProperty from both arguments, so a value of a wider or nullable type, such as
        // an int? parameter for an int column, converts the target instead: t => (int?)t.Count.
        // The SQL would write that NULL or that long into the column.
        var setType = call.TargetMethod.TypeArguments[0];
        if (member.Type is { } columnType && !SymbolEqualityComparer.Default.Equals(columnType, setType))
        {
            context.Report(LinqDiagnostics.Unsupported(
                "'" + member.Syntax + "' as a setter target of type '" + setType.ToDisplayString()
                    + "', a conversion of its column's '" + columnType.ToDisplayString() + "',",
                member.Syntax.GetLocation()));
            return false;
        }

        // The overload whose value is a lambda over the row, Func<T, TProperty>, rather than TProperty.
        SqlExpression? value;
        IOperation valueOperation;
        var valueArgument = ArgumentFor(call, 1);
        if (call.TargetMethod.OriginalDefinition.Parameters[1].Type.TypeKind == TypeKind.Delegate)
        {
            if (!TryReadLambda(valueArgument, out var valueRow, out var valueBody))
                return Unsupported(call, "'SetProperty' with a value that is not a lambda", context);
            valueOperation = Unwrap(valueBody);
            value = ReadSetterValue(valueBody, valueRow, context);
        }
        else
        {
            valueOperation = Unwrap(valueArgument);
            value = ReadSetterValue(valueArgument, row: null, context);
        }
        if (value is null) return false;

        if (json)
        {
            // A jsonb column takes a document: a parameter, which binds as jsonb, a constant or
            // null, which PostgreSQL types from the column, or another [Json] column. A text
            // column's value would be rejected at run time, 42804.
            var document = value is SqlParameterExpression or SqlLiteralExpression
                || (value is SqlMemberExpression
                    && valueOperation is IPropertyReferenceOperation { Property: var source }
                    && OrmGenerator.IsJsonString(source, source.Type));
            if (!document)
            {
                context.Report(LinqDiagnostics.Unsupported(
                    "'" + valueOperation.Syntax + "' set into the [Json] '" + target.Member
                        + "', rather than a parameter, a constant, null or another [Json] column,",
                    valueOperation.Syntax.GetLocation()));
                return false;
            }

            // The parameter binds as jsonb, as a [Json] parameter would.
            if (value is SqlParameterExpression parameter) context.JsonParameters.Add(parameter.ClrName);
            chain.JsonSetters.Add(chain.Setters.Count);
        }

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
        if (IsXminCall(op))
        {
            context.Report(LinqDiagnostics.Unsupported(
                "'" + op.Syntax + "' as a setter target, since xmin is a system column PostgreSQL sets itself,",
                op.Syntax.GetLocation()));
            return null;
        }
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
