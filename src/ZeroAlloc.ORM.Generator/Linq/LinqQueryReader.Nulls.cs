using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.ORM.Generator.Linq;

// Comparisons follow C#, not SQL's three-valued logic. In C# null == null is true, null == 5
// is false, and null > 5 is false; in SQL each of them is NULL. The reader knows which
// operands can be null, from the entity property's or the companion parameter's declared
// type, nullable reference annotations included, and adds EF-style compensation:
//
//   a == b, both can be null   (a = b OR (a IS NULL AND b IS NULL))
//   a != b, both can be null   ((a <> b OR (a IS NULL AND b IS NOT NULL)) OR (a IS NOT NULL AND b IS NULL))
//   a != b, only a can be null (a <> b OR a IS NULL)
//   !c, c can be NULL          (c) IS NOT TRUE
//
// A == with one nullable side needs nothing: NULL = 5 is NULL, which a WHERE drops, as C#'s
// false would. A relational comparison is the same in a positive position. A NULL survives
// AND and OR with the meaning "false", so only NOT, which would keep it NULL, needs IS NOT TRUE.
// In a context without nullable annotations, a reference type counts as not null.
internal static partial class LinqQueryReader
{
    // Whether a value of this declared type can be null: Nullable<T>, or a reference type or
    // type parameter annotated with `?`.
    private static bool CanBeNull(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
            || ((type.IsReferenceType || type is ITypeParameterSymbol) && type.NullableAnnotation == NullableAnnotation.Annotated);

    // A comparison of two read operands, compensated as above.
    private static SqlBinaryExpression Compare(SqlBinaryOperator compare, SqlExpression left, SqlExpression right, ReaderContext context)
    {
        var comparison = new SqlBinaryExpression(compare, left, right);
        var leftNull = MayBeNull(left, context.NullableValues);
        var rightNull = MayBeNull(right, context.NullableValues);
        return compare switch
        {
            SqlBinaryOperator.Equal when leftNull && rightNull
                => Or(comparison, And(IsNull(left), IsNull(right))),
            SqlBinaryOperator.NotEqual when leftNull && rightNull
                => Or(Or(comparison, And(IsNull(left), IsNotNull(right))), And(IsNotNull(left), IsNull(right))),
            SqlBinaryOperator.NotEqual when leftNull => Or(comparison, IsNull(left)),
            SqlBinaryOperator.NotEqual when rightNull => Or(comparison, IsNull(right)),
            _ => comparison,
        };
    }

    // C#'s `!`: NOT for a condition that is never NULL, IS NOT TRUE for one that can be.
    private static SqlExpression Negate(SqlExpression operand, ReaderContext context)
        => MayBeNull(operand, context.NullableValues) ? new SqlIsNotTrueExpression(operand) : new SqlNotExpression(operand);

    private static SqlBinaryExpression Or(SqlExpression left, SqlExpression right) => new(SqlBinaryOperator.Or, left, right);

    private static SqlBinaryExpression And(SqlExpression left, SqlExpression right) => new(SqlBinaryOperator.And, left, right);

    // No cast: the comparison next to the test is a use that types the parameter.
    private static SqlIsNullExpression IsNull(SqlExpression operand) => new(operand, Negated: false, Cast: null);

    private static SqlIsNullExpression IsNotNull(SqlExpression operand) => new(operand, Negated: true, Cast: null);

    /// <summary>
    /// Whether <paramref name="expression"/> can be NULL in SQL. A column or a parameter can when
    /// <paramref name="nullableValues"/> holds it; a condition can when an operand can. One case
    /// per SqlExpression record, as in the renderer.
    /// </summary>
    internal static bool MayBeNull(SqlExpression expression, HashSet<SqlExpression> nullableValues)
    {
        switch (expression)
        {
            case SqlMemberExpression or SqlParameterExpression:
                return nullableValues.Contains(expression);
            case SqlLiteralExpression literal:
                return literal.Kind == SqlLiteralKind.Null;
            case SqlEnumLiteralExpression or SqlIsNullExpression or SqlIsNotTrueExpression:
                return false;
            case SqlCastExpression cast:
                return MayBeNull(cast.Operand, nullableValues);
            case SqlDateTruncExpression dateTrunc:
                return MayBeNull(dateTrunc.Operand, nullableValues);
            case SqlBinaryExpression binary:
                return MayBeNull(binary.Left, nullableValues) || MayBeNull(binary.Right, nullableValues);
            case SqlRowComparisonExpression rows:
                return AnyMayBeNull(rows.Left, nullableValues) || AnyMayBeNull(rows.Right, nullableValues);
            case SqlNotExpression not:
                return MayBeNull(not.Operand, nullableValues);
            case SqlLikeExpression like:
                return MayBeNull(like.Operand, nullableValues) || MayBeNull(like.Value, nullableValues);
            case SqlILikeExpression ilike:
                return MayBeNull(ilike.Operand, nullableValues) || MayBeNull(ilike.Pattern, nullableValues);
            case SqlAnyExpression any:
                return MayBeNull(any.Operand, nullableValues) || MayBeNull(any.Array, nullableValues);
            case SqlConditionalExpression conditional:
                // (c) IS NOT TRUE is never NULL; when it is false, the predicate decides.
                return MayBeNull(conditional.Predicate, nullableValues);
            case SqlPagingCountExpression count:
                return MayBeNull(count.Count, nullableValues);
            case SqlAggregateCallExpression call:
                // Only a group's aggregate is read here, and a group has rows. Then a count is never
                // NULL, nor is a sum; Min, Max and Average are NULL when every value is, so when the
                // argument can be.
                return call.Function is not (SqlAggregateFunction.Count or SqlAggregateFunction.LongCount or SqlAggregateFunction.Sum)
                    && call.Argument is { } argument
                    && MayBeNull(argument, nullableValues);
            default:
                throw new InvalidOperationException("No nullability rule for " + expression.GetType().Name + ".");
        }
    }

    private static bool AnyMayBeNull(Model.EquatableArray<SqlExpression> items, HashSet<SqlExpression> nullableValues)
    {
        foreach (var item in items)
        {
            if (MayBeNull(item, nullableValues)) return true;
        }
        return false;
    }
}
