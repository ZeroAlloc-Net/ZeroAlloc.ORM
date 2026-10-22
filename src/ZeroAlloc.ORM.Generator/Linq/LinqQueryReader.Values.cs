using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using ZeroAlloc.ORM.Generator.Model;
using ZeroAlloc.TypeConversions;

namespace ZeroAlloc.ORM.Generator.Linq;

internal static partial class LinqQueryReader
{
    private const string StoreAsStringAttributeFullName = "ZeroAlloc.ORM.StoreAsStringAttribute";

    /// <summary>How reading one value went.</summary>
    private enum OperandRead
    {
        /// <summary>The value translated.</summary>
        Read,

        /// <summary>A diagnostic, such as ZAO103, was reported; the caller adds nothing.</summary>
        Reported,

        /// <summary>The form is not a value; the caller reports it in its own terms, ZAO102 or ZAO107.</summary>
        Unsupported,
    }

    // The value forms every clause shares: a member of the lambda's row parameter, a companion
    // parameter, and a compile-time constant. Anything else that names a symbol from outside the
    // chain is a capture, ZAO103. A later clause adds its own forms, such as comparisons, on top.
    // With no row parameter, as for a WhereIf condition, only parameters and constants are values.
    private static OperandRead ReadOperand(
        IOperation value,
        IParameterSymbol? row,
        ReaderContext context,
        out SqlExpression? expression)
    {
        var op = Unwrap(value);
        if (CompositeMemberType(op, row, context) is { } composite)
        {
            expression = null;
            context.Report(LinqDiagnostics.Unsupported(
                "'" + op.Syntax + "', whose type '" + composite.ToDisplayString() + "' maps to several columns,",
                op.Syntax.GetLocation()));
            return OperandRead.Reported;
        }

        if (NullableValueObjectValue(op, row, context) is { } valueOf)
        {
            expression = null;
            context.Report(LinqDiagnostics.Unsupported(
                "'" + valueOf.Syntax + "', the Value of a value object that can be null, which throws in C# "
                    + "where SQL would drop or null the row,",
                valueOf.Syntax.GetLocation()));
            return OperandRead.Reported;
        }

        // A member of a companion parameter, such as `minOrder.Value`, is not a value the SQL can
        // bind. It is no capture either: the parameter already is one. `p.HasValue` is read as a
        // condition, by ReadPredicate.
        if (op is IMemberReferenceOperation { Instance: IParameterReferenceOperation owner } member
            && context.SqlNames.ContainsKey(owner.Parameter))
        {
            expression = null;
            context.Report(LinqDiagnostics.Unsupported(
                "Reading '" + member.Member.Name + "' of companion parameter '" + owner.Parameter.Name
                    + "', rather than the parameter itself or a HasValue test,",
                op.Syntax.GetLocation()));
            return OperandRead.Reported;
        }

        expression = op.ConstantValue.HasValue ? ReadConstant(op) : ReadReference(op, row, context);
        if (expression is not null) return OperandRead.Read;
        if (!op.ConstantValue.HasValue && ReadsOuterRow(op, context))
        {
            context.Report(LinqDiagnostics.Unsupported(
                "'" + op.Syntax + "', a read of the outer row inside a subquery, which makes it a correlated subquery,",
                op.Syntax.GetLocation()));
            return OperandRead.Reported;
        }
        if (op.ConstantValue.HasValue || !IsCapture(op, row)) return OperandRead.Unsupported;

        context.Report(LinqDiagnostics.Capture(op.Syntax.ToString(), op.Syntax.GetLocation()));
        return OperandRead.Reported;
    }

    // A setter's value: the forms every clause reads, a column of the row, a companion parameter
    // or a constant, plus null, and +, - and * over those on numbers. Arithmetic is read here
    // only; a predicate keeps rejecting it. Without a row parameter, as for SetProperty's plain
    // value, only parameters and constants are values. Returns null after reporting.
    private static SqlExpression? ReadSetterValue(IOperation value, IParameterSymbol? row, ReaderContext context)
    {
        var op = Unwrap(value);
        if (IsNullValue(op)) return SqlLiteralExpression.Null;
        if (!op.ConstantValue.HasValue && op is IBinaryOperation binary) return ReadArithmetic(binary, row, context);

        switch (ReadOperand(value, row, context, out var expression))
        {
            case OperandRead.Read:
                return expression;
            case OperandRead.Unsupported:
                context.Report(LinqDiagnostics.Unsupported(
                    "'" + op.Syntax + "' as a setter value, rather than a companion parameter, a constant, a column of the row, "
                        + "or +, - and * over those,",
                    op.Syntax.GetLocation()));
                return null;
            default:
                return null;
        }
    }

    // `null`, written bare or cast, as `(DateTimeOffset?)null`, or `default` of a type that can be
    // null: the NULL literal. An explicit cast of a null to Nullable<T> is not a constant.
    private static bool IsNullValue(IOperation op) => op switch
    {
        _ when op.ConstantValue is { HasValue: true, Value: null } => true,
        IConversionOperation { Conversion.IsUserDefined: false, Operand: var operand } => IsNullValue(Unwrap(operand)),
        IDefaultValueOperation { Type: { } type } => CanBeNull(type) || type.IsReferenceType,
        _ => false,
    };

    // C#'s built-in +, - and * on int, long, decimal, double and float, lifted ones included,
    // whose NULL operand makes the result NULL as in C#. Each operand is a setter value itself.
    private static SqlArithmeticExpression? ReadArithmetic(IBinaryOperation binary, IParameterSymbol? row, ReaderContext context)
    {
        var arithmetic = binary.OperatorKind switch
        {
            BinaryOperatorKind.Add => SqlArithmeticOperator.Add,
            BinaryOperatorKind.Subtract => SqlArithmeticOperator.Subtract,
            BinaryOperatorKind.Multiply => SqlArithmeticOperator.Multiply,
            _ => (SqlArithmeticOperator?)null,
        };
        if (arithmetic is not { } kind || !IsArithmeticOnNumbers(binary))
        {
            context.Report(LinqDiagnostics.Unsupported(
                "'" + binary.Syntax + "' in a setter value, where +, - and * apply to int, long, decimal, double and float only,",
                binary.Syntax.GetLocation()));
            return null;
        }

        var left = ReadSetterValue(binary.LeftOperand, row, context);
        if (left is null) return null;
        var right = ReadSetterValue(binary.RightOperand, row, context);
        return right is null ? null : new SqlArithmeticExpression(kind, left, right);
    }

    // The operator is the language's own: no user-defined one, though decimal's built-in
    // operators are bound to its op_ methods. The result, and so both promoted operands, is one
    // of the five numeric types; a string + is a concatenation and an enum + is not a number.
    // Each operand is one too, before C#'s promotion: a char, a short or a uint promoted to int
    // or long would meet PostgreSQL's own type for it, such as a smallint, rather than C#'s. A
    // null operand has no type and makes the result NULL.
    private static bool IsArithmeticOnNumbers(IBinaryOperation binary)
        => (binary.OperatorMethod is null || binary.OperatorMethod.ContainingType?.SpecialType == SpecialType.System_Decimal)
            && binary.Type is { } type
            && IsArithmeticType(type)
            && IsArithmeticOperand(binary.LeftOperand)
            && IsArithmeticOperand(binary.RightOperand);

    private static bool IsArithmeticOperand(IOperation operand)
        => Unwrap(operand) is var op
            && (op.Type is { } type ? IsArithmeticType(type) : IsNullValue(op));

    private static bool IsArithmeticType(ITypeSymbol type)
        => UnwrapNullable(type).SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64
            or SpecialType.System_Decimal or SpecialType.System_Double or SpecialType.System_Single;

    // A column or a parameter, recorded in NullableValues when its declared type can be null.
    private static SqlExpression? ReadReference(IOperation op, IParameterSymbol? row, ReaderContext context)
    {
        ITypeSymbol type;
        SqlExpression expression;
        if (TryReadRowMember(op, row, context, out var name, out type, out var alias))
        {
            expression = Member(name, op, type, alias);
            if (IsLiftedOwnedMember(op, type)) context.LiftedValues.Add(expression);
        }
        else if (op is IParameterReferenceOperation p && context.SqlNames.TryGetValue(p.Parameter, out var sqlName))
        {
            type = p.Parameter.Type;
            expression = new SqlParameterExpression(
                sqlName,
                p.Parameter.Name,
                ColumnPicksEnumStorage(type),
                LocationInfo.From(op.Syntax.GetLocation()));
        }
        else
        {
            return null;
        }

        if (CanBeNull(type)) context.NullableValues.Add(expression);
        return expression;
    }

    // A member of an optional owned object whose declared type cannot be null, read with the
    // lifted type TryReadRowMember gave it, such as r.Outcome!.HasFailedValidation as bool?.
    private static bool IsLiftedOwnedMember(IOperation op, ITypeSymbol readType)
        => op is IPropertyReferenceOperation { Property: var property, Instance: IPropertyReferenceOperation { Property.NullableAnnotation: NullableAnnotation.Annotated } }
            && !CanBeNull(property.Type)
            && CanBeNull(readType);

    // The type of a row member that is a composite, such as Money(decimal Amount, string
    // Currency): the [Query] path maps it to one column per constructor argument, so it is not
    // one column the SQL can name. Null for any other value.
    private static ITypeSymbol? CompositeMemberType(IOperation op, IParameterSymbol? row, ReaderContext context)
    {
        return TryReadRowMember(op, row, context, out _, out var type, out _) && IsComposite(type, context.Conventions)
            ? type
            : null;
    }

    // A multi-field composite: the [Query] path binds it as one DbParameter per constructor
    // argument, @p_Amount and @p_Currency, and reads it from as many columns. A type with
    // [Materialize(Factory = ...)] counts too, whatever convention it resolves to: the read
    // side expands it into the factory's inner columns.
    private static bool IsComposite(ITypeSymbol type, ConventionContext conventions)
    {
        var underlying = UnwrapNullable(type);
        return ConventionDiscovery.Resolve(underlying, conventions).Kind == ConventionKind.MultiArgCtor
            || OrmGenerator.ReadMaterializeFactoryName(underlying.GetAttributes()) is not null;
    }

    private static SqlMemberExpression Member(string name, IOperation op, ITypeSymbol type, string? alias)
        => new(name, LocationInfo.From(op.Syntax.GetLocation()), ColumnPicksEnumStorage(type), alias);

    private static bool IsParameter(IParameterReferenceOperation reference, IParameterSymbol? parameter)
        => parameter is not null && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter);

    // An enum, or a nullable one, without [StoreAsString]: its storage follows the columns.
    private static bool ColumnPicksEnumStorage(ITypeSymbol type)
        => UnwrapNullable(type) is INamedTypeSymbol { TypeKind: TypeKind.Enum } named && !StoresName(named);

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

    private static bool StoresName(INamedTypeSymbol enumType)
        => enumType.GetAttributes().Any(a => string.Equals(
            a.AttributeClass?.ToDisplayString(), StoreAsStringAttributeFullName, StringComparison.Ordinal));

    // A local, a field or property not reached through the row parameter, or `this`. With no row
    // parameter, as for the chain source, every field and property is a capture.
    private static bool IsCapture(IOperation op, IParameterSymbol? row) => op switch
    {
        ILocalReferenceOperation or IInstanceReferenceOperation => true,
        IFieldReferenceOperation field => !IsRooted(field.Instance, row),
        IPropertyReferenceOperation property => !IsRooted(property.Instance, row),
        _ => false,
    };

    // Whether a member access chain starts at the row parameter, as `t.Project.Name` does.
    private static bool IsRooted(IOperation? instance, IParameterSymbol? row) => instance switch
    {
        IParameterReferenceOperation p => IsParameter(p, row),
        IMemberReferenceOperation member => IsRooted(member.Instance, row),
        _ => false,
    };

    // A compile-time constant as a SQL literal: null, booleans, numbers, strings and enum
    // members. Null when the constant has no literal form, such as a char or NaN.
    private static SqlExpression? ReadConstant(IOperation op)
    {
        var value = op.ConstantValue.Value;
        var enumType = op.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } named ? named : null;
        return value switch
        {
            null => SqlLiteralExpression.Null,
            _ when enumType is not null => ReadEnumConstant(enumType, value, op),
            bool flag => new SqlLiteralExpression(SqlLiteralKind.Boolean, flag ? "true" : "false"),
            string text => new SqlLiteralExpression(SqlLiteralKind.String, text),
            double d => Finite(d) ? SqlLiteralExpression.Number(d.ToString("R", CultureInfo.InvariantCulture)) : null,
            float f => Finite(f) ? new SqlLiteralExpression(SqlLiteralKind.Real, f.ToString("R", CultureInfo.InvariantCulture)) : null,
            sbyte or byte or short or ushort or int or uint or long or ulong or decimal
                => SqlLiteralExpression.Number(Convert.ToString(value, CultureInfo.InvariantCulture)!),
            _ => null,
        };
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    // An enum member renders as its integer, or as its name when the enum type is [StoreAsString]
    // or the column it is compared with is EnumAsString; the renderer decides. The read side
    // follows the same conventions, so the literal and the member agree. A [StoreAsString] value
    // that names no member has no literal form.
    private static SqlEnumLiteralExpression? ReadEnumConstant(INamedTypeSymbol enumType, object value, IOperation op)
    {
        var storesName = StoresName(enumType);
        var member = enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, value));
        if (storesName && member is null) return null;
        return new SqlEnumLiteralExpression(
            Convert.ToString(value, CultureInfo.InvariantCulture)!,
            member?.Name,
            storesName,
            LocationInfo.From(op.Syntax.GetLocation()));
    }
}
