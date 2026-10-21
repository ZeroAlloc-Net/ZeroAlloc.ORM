namespace ZeroAlloc.ORM.Linq;

/// <summary>LINQ-shaped operators read by the generator at build time. None of them run.</summary>
public static class QueryOperators
{
    /// <summary>Filters the query.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to extend.</param>
    /// <param name="predicate">The filter, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<T> Where<T>(this IQuery<T> source, Func<T, bool> predicate) => throw Guidance.Exception();

    /// <summary>Filters the query only when <paramref name="condition"/> holds at run time.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to extend.</param>
    /// <param name="condition">Whether to apply the filter.</param>
    /// <param name="predicate">The filter, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<T> WhereIf<T>(this IQuery<T> source, bool condition, Func<T, bool> predicate) => throw Guidance.Exception();

    /// <summary>Orders ascending.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <typeparam name="TKey">The ordering key type.</typeparam>
    /// <param name="source">The query to extend.</param>
    /// <param name="keySelector">The key, translated to a column.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IOrderedQuery<T> OrderBy<T, TKey>(this IQuery<T> source, Func<T, TKey> keySelector) => throw Guidance.Exception();

    /// <summary>Orders descending.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <typeparam name="TKey">The ordering key type.</typeparam>
    /// <param name="source">The query to extend.</param>
    /// <param name="keySelector">The key, translated to a column.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IOrderedQuery<T> OrderByDescending<T, TKey>(this IQuery<T> source, Func<T, TKey> keySelector) => throw Guidance.Exception();

    /// <summary>Adds an ascending tie-breaker.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <typeparam name="TKey">The ordering key type.</typeparam>
    /// <param name="source">The query to extend.</param>
    /// <param name="keySelector">The key, translated to a column.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IOrderedQuery<T> ThenBy<T, TKey>(this IOrderedQuery<T> source, Func<T, TKey> keySelector) => throw Guidance.Exception();

    /// <summary>Adds a descending tie-breaker.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <typeparam name="TKey">The ordering key type.</typeparam>
    /// <param name="source">The query to extend.</param>
    /// <param name="keySelector">The key, translated to a column.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IOrderedQuery<T> ThenByDescending<T, TKey>(this IOrderedQuery<T> source, Func<T, TKey> keySelector) => throw Guidance.Exception();

    /// <summary>Skips rows (OFFSET).</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to extend.</param>
    /// <param name="count">
    /// The number of rows to skip: a companion parameter, or a non-negative <see langword="int"/>
    /// literal. A negative parameter skips nothing, as in LINQ to Objects.
    /// </param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<T> Skip<T>(this IQuery<T> source, int count) => throw Guidance.Exception();

    /// <summary>Limits rows (LIMIT).</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to extend.</param>
    /// <param name="count">
    /// The maximum number of rows: a companion parameter, or a non-negative <see langword="int"/>
    /// literal. A negative parameter takes no rows, as in LINQ to Objects.
    /// </param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<T> Take<T>(this IQuery<T> source, int count) => throw Guidance.Exception();

    /// <summary>Projects each row.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <typeparam name="TResult">The projected element type.</typeparam>
    /// <param name="source">The query to extend.</param>
    /// <param name="selector">The projection, translated to columns.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<TResult> Select<T, TResult>(this IQuery<T> source, Func<T, TResult> selector) => throw Guidance.Exception();

    /// <summary>Counts rows.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to extend.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<int> Count<T>(this IQuery<T> source) => throw Guidance.Exception();

    /// <summary>Counts rows as a 64-bit integer.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to extend.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<long> LongCount<T>(this IQuery<T> source) => throw Guidance.Exception();

    /// <summary>Tests whether any row matches.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to extend.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<bool> Any<T>(this IQuery<T> source) => throw Guidance.Exception();
}
