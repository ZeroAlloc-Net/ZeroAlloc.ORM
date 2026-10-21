using Microsoft.CodeAnalysis;

namespace ZeroAlloc.ORM.Generator.Linq;

internal static partial class LinqQueryReader
{
    private const string TasksNamespace = "System.Threading.Tasks";
    private const string CollectionsNamespace = "System.Collections.Generic";

    // The result shape the method's return type asks for, given the companion's IQuery<TRow>;
    // null when it does not fit, which is ZAO112. A scalar terminal adds its shape here.
    private static LinqResultShape? ClassifyShape(ITypeSymbol returnType, ITypeSymbol rowType)
    {
        if (IsGeneric(returnType, CollectionsNamespace, "IAsyncEnumerable`1", out var streamed))
            return IsRow(streamed, rowType) ? LinqResultShape.Stream : null;
        if (!IsGeneric(returnType, TasksNamespace, "Task`1", out var result))
            return null;

        if (IsGeneric(result, CollectionsNamespace, "List`1", out var element)
            || IsGeneric(result, CollectionsNamespace, "IReadOnlyList`1", out element)
            || IsGeneric(result, CollectionsNamespace, "IList`1", out element))
        {
            return IsRow(element, rowType) ? LinqResultShape.List : null;
        }

        // [Query] reads one row only as a nullable reference: Task<TRow?>, null for no row.
        return IsRow(result, rowType)
            && result.IsReferenceType
            && result.NullableAnnotation == NullableAnnotation.Annotated
                ? LinqResultShape.SingleRow
                : null;
    }

    private static bool IsRow(ITypeSymbol candidate, ITypeSymbol rowType)
        => SymbolEqualityComparer.Default.Equals(candidate, rowType);
}
