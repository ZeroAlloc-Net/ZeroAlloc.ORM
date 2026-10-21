using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using ZeroAlloc.ORM.Generator.Model;

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
        expression = op.ConstantValue.HasValue ? ReadConstant(op) : ReadReference(op, row, context);
        if (expression is not null) return OperandRead.Read;
        if (op.ConstantValue.HasValue || !IsCapture(op, row)) return OperandRead.Unsupported;

        context.Report(LinqDiagnostics.Capture(op.Syntax.ToString(), op.Syntax.GetLocation()));
        return OperandRead.Reported;
    }

    private static SqlExpression? ReadReference(IOperation op, IParameterSymbol? row, ReaderContext context)
        => op switch
        {
            IPropertyReferenceOperation { Instance: IParameterReferenceOperation p, Arguments.Length: 0 } property
                when IsParameter(p, row) => Member(property.Property.Name, op, property.Property.Type),
            // A field is never a mapped column; the binder reports it as ZAO105.
            IFieldReferenceOperation { Instance: IParameterReferenceOperation p } field
                when IsParameter(p, row) => Member(field.Field.Name, op, field.Field.Type),
            IParameterReferenceOperation p when context.SqlNames.TryGetValue(p.Parameter, out var sqlName)
                => new SqlParameterExpression(
                    sqlName,
                    p.Parameter.Name,
                    ColumnPicksEnumStorage(p.Parameter.Type),
                    LocationInfo.From(op.Syntax.GetLocation())),
            _ => null,
        };

    private static SqlMemberExpression Member(string name, IOperation op, ITypeSymbol type)
        => new(name, LocationInfo.From(op.Syntax.GetLocation()), ColumnPicksEnumStorage(type));

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
            float f => Finite(f) ? SqlLiteralExpression.Number(f.ToString("R", CultureInfo.InvariantCulture)) : null,
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
