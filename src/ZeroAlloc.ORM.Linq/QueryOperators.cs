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

    /// <summary>
    /// Tests whether the query's one selected column holds <paramref name="value"/>, inside a
    /// predicate: <c>value IN (SELECT ...)</c>, with the query's own filter, order and page.
    /// </summary>
    /// <typeparam name="T">The type of the selected column.</typeparam>
    /// <param name="source">
    /// A full <c>Sql.Table&lt;T&gt;()</c> chain ending in a <c>Select</c> of one non-nullable column,
    /// which does not read the outer row.
    /// </param>
    /// <param name="value">
    /// The outer value to look for. A null value is not contained, as in LINQ to Objects over a
    /// set without null.
    /// </param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static bool Contains<T>(this IQuery<T> source, T value) => throw Guidance.Exception();

    /// <summary>
    /// Deletes the rows the chain selects: <c>DELETE FROM table WHERE ...</c>. Only <c>Where</c> and
    /// <c>WhereIf</c> may come before it; without either it deletes every row. Ends the chain of a
    /// <see cref="LinqCommandAttribute"/> companion.
    /// </summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The rows to delete.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static int ExecuteDelete<T>(this IQuery<T> source) => throw Guidance.Exception();

    /// <summary>
    /// Updates the rows the chain selects: <c>UPDATE table SET a = ..., b = ... WHERE ...</c>, one
    /// <c>SET</c> item per <see cref="SetPropertyCalls{T}.SetProperty{TProperty}(Func{T, TProperty}, TProperty)"/>
    /// in written order. Only <c>Where</c> and <c>WhereIf</c> may come before it; without either
    /// it updates every row. Ends the chain of a <see cref="LinqCommandAttribute"/> companion.
    /// </summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The rows to update.</param>
    /// <param name="setters">The setters, such as <c>set =&gt; set.SetProperty(t =&gt; t.State, state)</c>.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static int ExecuteUpdate<T>(this IQuery<T> source, Func<SetPropertyCalls<T>, SetPropertyCalls<T>> setters) => throw Guidance.Exception();

    /// <summary>Groups rows by a key; read the group's <c>Key</c> and aggregates in a following Select.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <param name="source">The query to group.</param>
    /// <param name="key">The key, translated to a column or composite key.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<IGroup<TKey, T>> GroupBy<T, TKey>(this IQuery<T> source, Func<T, TKey> key) => throw Guidance.Exception();

    /// <summary>Inner-joins two queries on a key.</summary>
    /// <typeparam name="TOuter">The element type of the outer query.</typeparam>
    /// <typeparam name="TInner">The element type of the inner query.</typeparam>
    /// <typeparam name="TKey">The join key type.</typeparam>
    /// <typeparam name="TResult">The projected element type of the join.</typeparam>
    /// <param name="outer">The outer query.</param>
    /// <param name="inner">The inner query.</param>
    /// <param name="outerKey">The outer join key, translated to a column.</param>
    /// <param name="innerKey">The inner join key, translated to a column.</param>
    /// <param name="result">The projection over the joined pair, translated to columns.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<TResult> Join<TOuter, TInner, TKey, TResult>(this IQuery<TOuter> outer, IQuery<TInner> inner, Func<TOuter, TKey> outerKey, Func<TInner, TKey> innerKey, Func<TOuter, TInner, TResult> result) => throw Guidance.Exception();

    /// <summary>Sums the selected int column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<int> Sum<T>(this IQuery<T> source, Func<T, int> selector) => throw Guidance.Exception();

    /// <summary>Sums the selected long column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<long> Sum<T>(this IQuery<T> source, Func<T, long> selector) => throw Guidance.Exception();

    /// <summary>Sums the selected decimal column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<decimal> Sum<T>(this IQuery<T> source, Func<T, decimal> selector) => throw Guidance.Exception();

    /// <summary>Sums the selected double column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<double> Sum<T>(this IQuery<T> source, Func<T, double> selector) => throw Guidance.Exception();

    /// <summary>Sums the selected int? column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<int?> Sum<T>(this IQuery<T> source, Func<T, int?> selector) => throw Guidance.Exception();

    /// <summary>Sums the selected long? column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<long?> Sum<T>(this IQuery<T> source, Func<T, long?> selector) => throw Guidance.Exception();

    /// <summary>Sums the selected decimal? column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<decimal?> Sum<T>(this IQuery<T> source, Func<T, decimal?> selector) => throw Guidance.Exception();

    /// <summary>Sums the selected double? column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<double?> Sum<T>(this IQuery<T> source, Func<T, double?> selector) => throw Guidance.Exception();

    /// <summary>Takes the smallest selected value.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <typeparam name="TValue">The value type of the selected column.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<TValue> Min<T, TValue>(this IQuery<T> source, Func<T, TValue> selector) => throw Guidance.Exception();

    /// <summary>Takes the largest selected value.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <typeparam name="TValue">The value type of the selected column.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<TValue> Max<T, TValue>(this IQuery<T> source, Func<T, TValue> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected int column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<double> Average<T>(this IQuery<T> source, Func<T, int> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected long column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<double> Average<T>(this IQuery<T> source, Func<T, long> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected decimal column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<decimal> Average<T>(this IQuery<T> source, Func<T, decimal> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected double column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<double> Average<T>(this IQuery<T> source, Func<T, double> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected int? column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<double?> Average<T>(this IQuery<T> source, Func<T, int?> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected long? column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<double?> Average<T>(this IQuery<T> source, Func<T, long?> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected decimal? column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<decimal?> Average<T>(this IQuery<T> source, Func<T, decimal?> selector) => throw Guidance.Exception();

    /// <summary>Averages the selected double? column.</summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to reduce to a single value.</param>
    /// <param name="selector">The column to aggregate, translated to SQL.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<double?> Average<T>(this IQuery<T> source, Func<T, double?> selector) => throw Guidance.Exception();
}
