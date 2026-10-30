using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

internal static partial class LinqQueryReader
{
    private const string TasksNamespace = "System.Threading.Tasks";
    private const string CollectionsNamespace = "System.Collections.Generic";

    // The result shape the method's return type asks for, given the companion's IQuery<TRow>;
    // null when it does not fit, which is ZAO112. `scalarRow` is whether TRow is a value one
    // column reads, such as a string or an int, rather than a record: then the rows are one
    // Select's values, read only as a list, or a terminal's one value. Which of the two the
    // chain gives is known once it is read; Read checks the shape against it.
    private static LinqResultShape? ClassifyShape(ITypeSymbol returnType, ITypeSymbol rowType, bool scalarRow)
    {
        if (IsGeneric(returnType, CollectionsNamespace, "IAsyncEnumerable`1", out var streamed))
            return !scalarRow && IsRow(streamed, rowType) ? LinqResultShape.Stream : null;
        // Task<T> and ValueTask<T> take the same shapes, as they do for [Query].
        if (!IsGeneric(returnType, TasksNamespace, "Task`1", out var result)
            && !IsGeneric(returnType, TasksNamespace, "ValueTask`1", out result))
        {
            return null;
        }
        return ClassifyResult(result, rowType);
    }

    // The shape of the T in Task<T>, or of one element of a tuple: every row, the first row or
    // the terminal's one value. Null when it does not fit.
    private static LinqResultShape? ClassifyResult(ITypeSymbol result, ITypeSymbol rowType)
    {
        // A list of records, or of one Select's values: List<int> reads Select(t => t.Order),
        // and Read reports it after Count, whose IQuery<int> is the same.
        if (IsGeneric(result, CollectionsNamespace, "List`1", out var element)
            || IsGeneric(result, CollectionsNamespace, "IReadOnlyList`1", out element)
            || IsGeneric(result, CollectionsNamespace, "IList`1", out element))
        {
            return IsRow(element, rowType) ? LinqResultShape.List : null;
        }

        var counted = rowType.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Boolean;
        if (counted || IsTerminalValue(rowType))
        {
            // The one value of a terminal, read as Task<T> or ValueTask<T>. [LinqQuery] takes a
            // string terminal only as string?, null for no row and for NULL: Min and Max give
            // no string over no rows, see NonNullString.
            return IsRow(result, rowType)
                && (!result.IsReferenceType || result.NullableAnnotation == NullableAnnotation.Annotated)
                    ? LinqResultShape.Scalar
                    : null;
        }

        // [Query] reads one row only as a nullable reference: Task<TRow?>, null for no row.
        return IsRow(result, rowType)
            && result.IsReferenceType
            && result.NullableAnnotation == NullableAnnotation.Annotated
                ? LinqResultShape.SingleRow
                : null;
    }

    // Whether the method returns Task<string> or ValueTask<string> where the companion gives
    // IQuery<string>: ZAO112 then points to string?. Enumerable.Min<string> and EF Core give
    // null over no rows, so a non-null string has no correct value there.
    private static bool NonNullString(ITypeSymbol returnType, ITypeSymbol rowType)
        => rowType.SpecialType == SpecialType.System_String
            && (IsGeneric(returnType, TasksNamespace, "Task`1", out var result)
                || IsGeneric(returnType, TasksNamespace, "ValueTask`1", out result))
            && result.SpecialType == SpecialType.System_String
            && result.NullableAnnotation != NullableAnnotation.Annotated;

    // The values a terminal gives that [Query] reads as a scalar: int, long and bool from Count,
    // LongCount and Any; and from Sum, Average, Min and Max an int, long, double or decimal
    // through Convert, a DateTime or DateTimeOffset through the one-column reader, either kind
    // nullable through the nullable scalar's, or a string. A non-null Average, Min or Max throws
    // over no rows, as LINQ's non-nullable overloads do. Others, such as Guid, which PostgreSQL
    // has no min for, are ZAO112.
    private static bool IsTerminalValue(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            return IsAggregateValue(nullable.TypeArguments[0]);
        return IsAggregateValue(type)
            || type.SpecialType is SpecialType.System_Boolean or SpecialType.System_String;
    }

    private static bool IsAggregateValue(ITypeSymbol type)
        => IsAggregateNumber(type.SpecialType)
            || type.SpecialType == SpecialType.System_DateTime
            || IsDateTimeOffset(type);

    private static bool IsAggregateNumber(SpecialType type)
        => type is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Double or SpecialType.System_Decimal;

    private static bool IsDateTimeOffset(ITypeSymbol type)
        => string.Equals(type.ToDisplayString(), "System.DateTimeOffset", StringComparison.Ordinal);

    private static bool IsRow(ITypeSymbol candidate, ITypeSymbol rowType)
        => SymbolEqualityComparer.Default.Equals(candidate, rowType);

    // A [LinqCommand] method's return type: Task or ValueTask, or Task<int> or ValueTask<int> for
    // the rows affected, the shapes the [Command] non-query path runs.
    private static bool IsNonQuery(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol { IsGenericType: false } plain
            && string.Equals(plain.ContainingNamespace?.ToDisplayString(), TasksNamespace, StringComparison.Ordinal)
            && plain.Name is "Task" or "ValueTask")
        {
            return true;
        }
        return (IsGeneric(returnType, TasksNamespace, "Task`1", out var result)
                || IsGeneric(returnType, TasksNamespace, "ValueTask`1", out result))
            && result.SpecialType == SpecialType.System_Int32;
    }

    // What a [LinqCommand] method can return.
    private const string CommandShapes =
        "return Task or ValueTask, or Task<int> or ValueTask<int> for the number of rows affected";

    // The element shapes of a tuple companion's method: Task<(...)> or ValueTask<(...)>, the
    // tuple optionally nullable, with one element per companion element, each fitting its
    // element's IQuery<T>. Null with the ZAO112 that names what does not fit.
    private static ImmutableArray<LinqResultShape>? ClassifyTupleShapes(
        IMethodSymbol method,
        MethodDeclarationSyntax syntax,
        IMethodSymbol companion,
        ImmutableArray<ITypeSymbol> rowTypes,
        out DiagnosticInfo? mismatch)
    {
        mismatch = null;
        var returnType = method.ReturnType;
        var methodTuple = (IsGeneric(returnType, TasksNamespace, "Task`1", out var result)
            || IsGeneric(returnType, TasksNamespace, "ValueTask`1", out result))
                ? UnwrapNullable(result) as INamedTypeSymbol
                : null;
        if (methodTuple is not { IsTupleType: true })
        {
            mismatch = TupleMismatch(method, syntax, companion, "'" + returnType.ToDisplayString() + "'",
                syntax.ReturnType.GetLocation(), "a tuple companion's method returns Task or ValueTask of a tuple; ");
            return null;
        }

        var elements = methodTuple.TupleElements;
        if (elements.Length != rowTypes.Length)
        {
            mismatch = TupleMismatch(method, syntax, companion, "'" + returnType.ToDisplayString() + "'",
                syntax.ReturnType.GetLocation(),
                "its tuple has " + elements.Length.ToString(CultureInfo.InvariantCulture) + " elements and the companion's "
                    + rowTypes.Length.ToString(CultureInfo.InvariantCulture) + "; ");
            return null;
        }

        var tupleSyntax = syntax.ReturnType.DescendantNodesAndSelf().OfType<TupleTypeSyntax>().FirstOrDefault();
        var shapes = ImmutableArray.CreateBuilder<LinqResultShape>(elements.Length);
        for (var i = 0; i < elements.Length; i++)
        {
            if (ClassifyResult(elements[i].Type, rowTypes[i]) is { } shape)
            {
                shapes.Add(shape);
                continue;
            }
            mismatch = ElementMismatch(method.Name, syntax, companion, elements[i], i, tupleSyntax);
            return null;
        }
        return shapes.MoveToImmutable();
    }

    // ZAO112 for one tuple element, at that element of the method's return type.
    private static DiagnosticInfo ElementMismatch(
        string methodName,
        MethodDeclarationSyntax syntax,
        IMethodSymbol companion,
        IFieldSymbol element,
        int index,
        TupleTypeSyntax? tupleSyntax)
    {
        var companionElement = ((INamedTypeSymbol)companion.ReturnType).TupleElements[index];
        var location = tupleSyntax is not null && index < tupleSyntax.Elements.Count
            ? tupleSyntax.Elements[index].GetLocation()
            : syntax.ReturnType.GetLocation();
        return LinqDiagnostics.ShapeMismatchAt(
            methodName,
            "'" + element.Type.ToDisplayString() + "' as tuple element '" + element.Name + "'",
            "'" + companionElement.Type.ToDisplayString() + "'",
            TupleShapes,
            location);
    }

    private static DiagnosticInfo TupleMismatch(
        IMethodSymbol method,
        MethodDeclarationSyntax syntax,
        IMethodSymbol companion,
        string returned,
        Location location,
        string reason)
        => LinqDiagnostics.ShapeMismatchAt(
            method.Name,
            returned,
            "'" + companion.ReturnType.ToDisplayString() + "'",
            reason + TupleShapes,
            location);

    // What each element of a tuple companion's method can be.
    private const string TupleShapes =
        "return one tuple element per companion element: List<T>, IReadOnlyList<T>, IList<T> or T?, with T the element's row type, "
        + "or after a terminal the value it gives, as for a method of its own";

    // The elements of the tuple a tuple companion's method returns, which ClassifyTupleShapes
    // has checked.
    private static ImmutableArray<IFieldSymbol> TupleElementsOf(IMethodSymbol method)
    {
        if (!IsGeneric(method.ReturnType, TasksNamespace, "Task`1", out var result))
            IsGeneric(method.ReturnType, TasksNamespace, "ValueTask`1", out result);
        return ((INamedTypeSymbol)UnwrapNullable(result)).TupleElements;
    }
}
