namespace ZeroAlloc.ORM.Linq;

/// <summary>Aggregate operators over one group produced by <c>GroupBy</c>. None of them run.</summary>
public static class GroupOperators
{
    /// <summary>Counts the rows in the group.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to count.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static int Count<TKey, T>(this IGroup<TKey, T> source) => throw Guidance.Exception();

    /// <summary>Counts the rows in the group as a 64-bit integer.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to count.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static long LongCount<TKey, T>(this IGroup<TKey, T> source) => throw Guidance.Exception();

    /// <summary>Sums the selected int column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static int Sum<TKey, T>(this IGroup<TKey, T> source, Func<T, int> selector) => throw Guidance.Exception();

    /// <summary>Sums the selected long column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static long Sum<TKey, T>(this IGroup<TKey, T> source, Func<T, long> selector) => throw Guidance.Exception();

    /// <summary>Sums the selected decimal column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static decimal Sum<TKey, T>(this IGroup<TKey, T> source, Func<T, decimal> selector) => throw Guidance.Exception();

    /// <summary>Sums the selected double column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static double Sum<TKey, T>(this IGroup<TKey, T> source, Func<T, double> selector) => throw Guidance.Exception();

    /// <summary>Sums the selected int? column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static int? Sum<TKey, T>(this IGroup<TKey, T> source, Func<T, int?> selector) => throw Guidance.Exception();

    /// <summary>Sums the selected long? column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static long? Sum<TKey, T>(this IGroup<TKey, T> source, Func<T, long?> selector) => throw Guidance.Exception();

    /// <summary>Sums the selected decimal? column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static decimal? Sum<TKey, T>(this IGroup<TKey, T> source, Func<T, decimal?> selector) => throw Guidance.Exception();

    /// <summary>Sums the selected double? column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static double? Sum<TKey, T>(this IGroup<TKey, T> source, Func<T, double?> selector) => throw Guidance.Exception();

    /// <summary>Takes the smallest selected value.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <typeparam name="TValue">The value type of the selected column.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static TValue Min<TKey, T, TValue>(this IGroup<TKey, T> source, Func<T, TValue> selector) => throw Guidance.Exception();

    /// <summary>Takes the largest selected value.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <typeparam name="TValue">The value type of the selected column.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static TValue Max<TKey, T, TValue>(this IGroup<TKey, T> source, Func<T, TValue> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected int column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static double Average<TKey, T>(this IGroup<TKey, T> source, Func<T, int> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected long column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static double Average<TKey, T>(this IGroup<TKey, T> source, Func<T, long> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected decimal column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static decimal Average<TKey, T>(this IGroup<TKey, T> source, Func<T, decimal> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected double column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static double Average<TKey, T>(this IGroup<TKey, T> source, Func<T, double> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected int? column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static double? Average<TKey, T>(this IGroup<TKey, T> source, Func<T, int?> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected long? column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static double? Average<TKey, T>(this IGroup<TKey, T> source, Func<T, long?> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected decimal? column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static decimal? Average<TKey, T>(this IGroup<TKey, T> source, Func<T, decimal?> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected double? column.</summary>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <typeparam name="T">The element type of the group.</typeparam>
    /// <param name="source">The group to aggregate.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static double? Average<TKey, T>(this IGroup<TKey, T> source, Func<T, double?> selector) => throw Guidance.Exception();
}
