namespace ZeroAlloc.ORM.Linq;

/// <summary>Entry points and SQL-only helpers for a LINQ companion method. None of them run.</summary>
public static class Sql
{
    /// <summary>Starts a query over the table mapped for <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The mapped entity type.</typeparam>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static IQuery<T> Table<T>() => throw Guidance.Exception();

    /// <summary>Case-insensitive pattern match, emitted as <c>ILIKE</c> where the dialect supports it.</summary>
    /// <param name="value">The value to test.</param>
    /// <param name="pattern">The pattern, using the SQL wildcard syntax.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static bool ILike(string? value, string? pattern) => throw Guidance.Exception();

    /// <summary>
    /// The row's <c>xmin</c>, the id of the transaction that wrote this version of it. Read it with
    /// the row, then guard a write with <c>Sql.Xmin(r) == expected</c>: once another writer changed
    /// the row, the write affects no rows. Compared by <c>==</c> or <c>!=</c> only, with a
    /// <c>uint</c> parameter, which binds as <c>xid</c>.
    /// </summary>
    /// <typeparam name="T">The mapped entity type.</typeparam>
    /// <param name="row">The row, the lambda's parameter or a joined source.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static uint Xmin<T>(T row) => throw Guidance.Exception();

    /// <summary>Builds a two-column row value for keyset comparison.</summary>
    /// <typeparam name="T1">The first column type.</typeparam>
    /// <typeparam name="T2">The second column type.</typeparam>
    /// <param name="item1">The first column.</param>
    /// <param name="item2">The second column.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static SqlRow<T1, T2> Row<T1, T2>(T1 item1, T2 item2) => throw Guidance.Exception();

    /// <summary>Builds a three-column row value for keyset comparison.</summary>
    /// <typeparam name="T1">The first column type.</typeparam>
    /// <typeparam name="T2">The second column type.</typeparam>
    /// <typeparam name="T3">The third column type.</typeparam>
    /// <param name="item1">The first column.</param>
    /// <param name="item2">The second column.</param>
    /// <param name="item3">The third column.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static SqlRow<T1, T2, T3> Row<T1, T2, T3>(T1 item1, T2 item2, T3 item3) => throw Guidance.Exception();
}
