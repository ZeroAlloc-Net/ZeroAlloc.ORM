using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using ZeroAlloc.ORM.Generator.Model;
using ZeroAlloc.TypeConversions;

namespace ZeroAlloc.ORM.Generator.Linq;

// Join(inner, outerKey, innerKey, result): an INNER JOIN. A query with a join aliases every
// source, t0 for the FROM and t1, t2, ... in join order, and every column it reads names its
// source's alias. The forms translated:
//
//   inner      Sql.Table<T>(), optionally followed by Where(...), whose conditions go into the ON
//   keys       one mapped column a side; a value object's .Value is its column, as everywhere
//   result     an anonymous type whose members are the two range variables, or members carried
//              over from an earlier join's result: new { et.Execution, et.Task, Project = p }
//
// The operators after a join read members of the result, such as x.Execution.InputTokens, which
// resolves through the RowShape to the alias of the source it belongs to. Anything else is ZAO102.
internal static partial class LinqQueryReader
{
    /// <summary>
    /// What a lambda's row is: one entity, whose members are columns of the source its alias
    /// names, or a join's anonymous result, whose members are themselves rows. Lives only
    /// during the read and holds no symbols.
    /// </summary>
    private sealed class RowShape
    {
        private RowShape(string? alias, Dictionary<string, RowShape>? members)
        {
            Alias = alias;
            Members = members;
        }

        // The source's alias; null for the FROM entity of a query without a join.
        public string? Alias { get; }

        // The anonymous members by name; null for an entity.
        public Dictionary<string, RowShape>? Members { get; }

        public bool IsEntity => Members is null;

        public static RowShape Entity(string? alias) => new(alias, members: null);

        public static RowShape Anonymous(Dictionary<string, RowShape> members) => new(alias: null, members);

        public RowShape? Member(string name)
            => Members is not null && Members.TryGetValue(name, out var shape) ? shape : null;
    }

    // Whether the chain joins anywhere along its spine, so its sources are aliased from the start.
    private static bool HasJoin(IOperation operation)
    {
        var op = Unwrap(operation);
        while (op is IInvocationOperation { Arguments.Length: > 0 } invocation && IsQueryOperator(invocation.TargetMethod))
        {
            if (string.Equals(invocation.TargetMethod.Name, "Join", StringComparison.Ordinal)) return true;
            op = Unwrap(invocation.Arguments[0].Value);
        }
        return false;
    }

    private static bool ReadJoin(IInvocationOperation invocation, ChainBuilder chain, ReaderContext context)
    {
        if (chain.Ordering.Count > 0) return Unsupported(invocation, "'Join' after 'OrderBy'", context);
        if (invocation.Arguments.Length != 5) return Unsupported(invocation, "'Join'", context);

        var alias = "t" + (chain.Joins.Count + 1).ToString(CultureInfo.InvariantCulture);
        var outer = context.Row;
        var inner = RowShape.Entity(alias);
        var outerKey = ReadJoinKey(ArgumentFor(invocation, 2), invocation, context);
        if (outerKey is null) return false;

        // The inner source's lambdas read the inner entity.
        var predicates = ImmutableArray.CreateBuilder<SqlExpression>();
        context.Row = inner;
        var source = ReadJoinSource(ArgumentFor(invocation, 1), predicates, context);
        var innerKey = source is null ? null : ReadJoinKey(ArgumentFor(invocation, 3), invocation, context);
        context.Row = outer;
        if (innerKey is null) return false;

        var result = ReadJoinResult(ArgumentFor(invocation, 4), outer, inner, invocation, context);
        if (result is null) return false;

        chain.Joins.Add(new SqlJoin(
            source!,
            alias,
            new SqlBinaryExpression(SqlBinaryOperator.Equal, outerKey, innerKey),
            new EquatableArray<SqlExpression>(predicates.ToImmutable())));
        context.Row = result;
        return true;
    }

    // Sql.Table<T>(), optionally followed by Where(...): the joined table, with the Where
    // conditions added to `predicates`. Any other operator on the inner side is ZAO102.
    private static SqlTableSource? ReadJoinSource(
        IOperation argument,
        ImmutableArray<SqlExpression>.Builder predicates,
        ReaderContext context)
    {
        var op = Unwrap(argument);
        if (op is not IInvocationOperation invocation)
        {
            context.Report(IsCapture(op, row: null)
                ? LinqDiagnostics.Capture(op.Syntax.ToString(), op.Syntax.GetLocation())
                : LinqDiagnostics.Unsupported("'" + op.Syntax + "' as the inner side of a 'Join'", op.Syntax.GetLocation()));
            return null;
        }

        var target = invocation.TargetMethod;
        if (IsLinqMember(target, SqlTypeName, "Table")) return TableSource(invocation);
        if (!IsQueryOperator(target)
            || !string.Equals(target.Name, "Where", StringComparison.Ordinal)
            || invocation.Arguments.Length != 2)
        {
            Unsupported(invocation, "'" + target.Name + "' on the inner side of a 'Join', where only 'Where' is translated,", context);
            return null;
        }

        var source = ReadJoinSource(invocation.Arguments[0].Value, predicates, context);
        if (source is null) return null;
        if (!TryReadLambda(invocation.Arguments[1].Value, out var row, out var body))
        {
            Unsupported(invocation, "'Where' without a lambda", context);
            return null;
        }
        var predicate = ReadPredicate(body, row, context);
        if (predicate is null) return null;
        AddConjuncts(predicates, predicate);
        return source;
    }

    // A key selector: one mapped column of its side, or a value object's .Value, which is the
    // same column. LINQ's Join never matches a null key, and neither does SQL's =.
    private static SqlMemberExpression? ReadJoinKey(IOperation argument, IInvocationOperation join, ReaderContext context)
    {
        if (!TryReadLambda(argument, out var row, out var returned))
        {
            Unsupported(join, "'Join' without a key lambda", context);
            return null;
        }

        var op = Unwrap(returned);
        if (op is IAnonymousObjectCreationOperation)
        {
            context.Report(LinqDiagnostics.Unsupported(
                "'" + op.Syntax + "', a composite join key,", op.Syntax.GetLocation()));
            return null;
        }

        var read = ReadOperand(op, row, context, out var key);
        if (read == OperandRead.Reported) return null;
        if (read == OperandRead.Read && key is SqlMemberExpression column) return column;
        context.Report(LinqDiagnostics.Unsupported(
            "'" + op.Syntax + "' as a join key, rather than a mapped column,", op.Syntax.GetLocation()));
        return null;
    }

    // Whether `property` is a value object's own Value, which reads its one column: declared on
    // a type that binds as one value, not on Nullable<T>, whose Value throws on a null in C#.
    private static bool IsValueObjectValue(IPropertySymbol property, ReaderContext context)
        => property is { Name: "Value", IsStatic: false }
            && property.ContainingType is { } owner
            && owner.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
            && ConventionDiscovery.Resolve(owner, context.Conventions).Kind
                is ConventionKind.ValueObject or ConventionKind.StaticFactory or ConventionKind.SingleArgCtor;

    // The `.Value` of a row member whose type is a value object of reference type annotated `?`,
    // such as l.Parent!.Value for `ProjectRef? Parent`; null for any other operation. In code
    // without nullable annotations the member counts as not null, as everywhere.
    private static IOperation? NullableValueObjectValue(IOperation op, IParameterSymbol? row, ReaderContext context)
        => op is IPropertyReferenceOperation { Arguments.Length: 0, Instance: { } wrapped } valueOf
            && IsValueObjectValue(valueOf.Property, context)
            && TryReadRowMember(wrapped, row, context, out _, out var type, out _)
            && IsNullableReference(type)
                ? op
                : null;

    private static bool IsNullableReference(ITypeSymbol type)
        => type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated;

    // The result selector's row: a range variable or a member carried over, or an anonymous
    // type of those. A computed member, or a column, is ZAO102 at the member.
    private static RowShape? ReadJoinResult(
        IOperation argument,
        RowShape outer,
        RowShape inner,
        IInvocationOperation join,
        ReaderContext context)
    {
        if (!TryReadLambda(argument, 2, out var parameters, out var returned))
        {
            Unsupported(join, "'Join' without a result lambda", context);
            return null;
        }

        var body = Unwrap(returned);
        if (body is not IAnonymousObjectCreationOperation anonymous)
            return JoinedRow(body, parameters, outer, inner) ?? ReportJoinMember(body, context);

        var members = new Dictionary<string, RowShape>(StringComparer.Ordinal);
        foreach (var initializer in anonymous.Initializers)
        {
            if (initializer is not ISimpleAssignmentOperation { Target: IPropertyReferenceOperation property, Value: var value }
                || JoinedRow(value, parameters, outer, inner) is not { } shape)
            {
                return ReportJoinMember(initializer, context);
            }
            members[property.Property.Name] = shape;
        }
        return RowShape.Anonymous(members);
    }

    private static RowShape? JoinedRow(IOperation value, ImmutableArray<IParameterSymbol> parameters, RowShape outer, RowShape inner)
    {
        var op = Unwrap(value);
        return ShapeAt(op, parameters[0], outer) ?? ShapeAt(op, parameters[1], inner);
    }

    private static RowShape? ReportJoinMember(IOperation at, ReaderContext context)
    {
        context.Report(LinqDiagnostics.Unsupported(
            "'" + at.Syntax + "' in a 'Join' result, rather than a joined source or a member carried over from an earlier join,",
            at.Syntax.GetLocation()));
        return null;
    }

    // The row `op` reaches from the lambda parameter `root`, whose row is `rootShape`: the
    // parameter itself, or a member path through anonymous results, such as et.Task.
    private static RowShape? ShapeAt(IOperation op, IParameterSymbol root, RowShape rootShape) => op switch
    {
        IParameterReferenceOperation reference when IsParameter(reference, root) => rootShape,
        IPropertyReferenceOperation { Arguments.Length: 0, Instance: { } owner } property
            => ShapeAt(owner, root, rootShape)?.Member(property.Property.Name),
        _ => null,
    };

    // A column of the row: a member of an entity row, reached from the lambda's row parameter
    // directly, as t.Title, or through a join's result, as x.Task.Title. Gives the member's
    // name, its declared type and its source's alias.
    private static bool TryReadRowMember(
        IOperation op,
        IParameterSymbol? row,
        ReaderContext context,
        out string name,
        out ITypeSymbol type,
        out string? alias)
    {
        // `t.ProjectId.Value` is the ProjectId column itself, when ProjectId is a row member that
        // cannot be null. A reference type annotated `?` can, and its Value throws in C# where SQL
        // drops or nulls the row; ReadOperand reports it, see NullableValueObjectValue.
        if (op is IPropertyReferenceOperation { Arguments.Length: 0, Instance: { } wrapped } valueOf
            && IsValueObjectValue(valueOf.Property, context)
            && TryReadRowMember(wrapped, row, context, out name, out type, out alias))
        {
            return !IsNullableReference(type);
        }

        name = null!;
        type = null!;
        alias = null;
        IOperation? owner;
        switch (op)
        {
            case IPropertyReferenceOperation { Arguments.Length: 0 } property:
                (name, type, owner) = (property.Property.Name, property.Property.Type, property.Instance);
                break;
            // A field is never a mapped column; the binder reports it as ZAO105.
            case IFieldReferenceOperation field:
                (name, type, owner) = (field.Field.Name, field.Field.Type, field.Instance);
                break;
            default:
                return false;
        }

        if (row is null || owner is null) return false;
        if (ShapeAt(owner, row, context.Row) is { IsEntity: true } shape)
        {
            alias = shape.Alias;
            return true;
        }

        // A member of an owned object, r.Repository.Url: the column the map keys by the path
        // Repository.Url. Whether the map declares Repository owned is the binder's to check; a
        // member it does not map is ZAO105 there. A member of an optional owned object, one
        // annotated `?`, is NULL when the object is absent, so its type is lifted: bool becomes
        // bool? and string becomes string?, and the null rules compensate for it. This differs
        // on purpose from the Value of a nullable value object, which is rejected: a member of an
        // optional owned object would otherwise be unusable.
        if (op is IPropertyReferenceOperation
            && owner is IPropertyReferenceOperation { Arguments.Length: 0, Instance: { } entity } ownedProperty
            && ShapeAt(entity, row, context.Row) is { IsEntity: true } entityShape
            && EntityMapReader.IsOwnable(ownedProperty.Property.Type, context.Conventions))
        {
            name = ownedProperty.Property.Name + "." + name;
            alias = entityShape.Alias;
            if (ownedProperty.Property.NullableAnnotation == NullableAnnotation.Annotated)
                type = Lifted(type, context);
            return true;
        }

        return false;
    }

    // The type as one that can be null: Nullable<T> for a value type, annotated for a reference type.
    private static ITypeSymbol Lifted(ITypeSymbol type, ReaderContext context)
    {
        if (CanBeNull(type)) return type;
        return type.IsValueType
            ? context.Conventions.Compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(type)
            : type.WithNullableAnnotation(NullableAnnotation.Annotated);
    }

    // A row member that holds an object whose properties are columns, such as an owned
    // r.Repository: a whole object, not one column. Null for any other operation.
    private static ITypeSymbol? WholeObjectMemberType(IOperation op, IParameterSymbol? row, ReaderContext context)
        => TryReadRowMember(op, row, context, out _, out var type, out _)
            && EntityMapReader.IsOwnable(type, context.Conventions)
                ? type
                : null;
}
