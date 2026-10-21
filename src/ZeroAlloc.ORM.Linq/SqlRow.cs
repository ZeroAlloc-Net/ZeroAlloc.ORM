namespace ZeroAlloc.ORM.Linq;

/// <summary>A two-column row value, compared lexicographically as <c>(a, b) &lt; (x, y)</c> in SQL.</summary>
/// <typeparam name="T1">The first column type.</typeparam>
/// <typeparam name="T2">The second column type.</typeparam>
public readonly struct SqlRow<T1, T2>
{
    /// <summary>Row-value less-than.</summary>
    /// <param name="left">The left row.</param>
    /// <param name="right">The right row.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static bool operator <(SqlRow<T1, T2> left, SqlRow<T1, T2> right) => throw Guidance.Exception();

    /// <summary>Row-value greater-than.</summary>
    /// <param name="left">The left row.</param>
    /// <param name="right">The right row.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static bool operator >(SqlRow<T1, T2> left, SqlRow<T1, T2> right) => throw Guidance.Exception();

    /// <summary>Row-value less-than-or-equal.</summary>
    /// <param name="left">The left row.</param>
    /// <param name="right">The right row.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static bool operator <=(SqlRow<T1, T2> left, SqlRow<T1, T2> right) => throw Guidance.Exception();

    /// <summary>Row-value greater-than-or-equal.</summary>
    /// <param name="left">The left row.</param>
    /// <param name="right">The right row.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static bool operator >=(SqlRow<T1, T2> left, SqlRow<T1, T2> right) => throw Guidance.Exception();
}

/// <summary>A three-column row value, compared lexicographically in SQL.</summary>
/// <typeparam name="T1">The first column type.</typeparam>
/// <typeparam name="T2">The second column type.</typeparam>
/// <typeparam name="T3">The third column type.</typeparam>
public readonly struct SqlRow<T1, T2, T3>
{
    /// <summary>Row-value less-than.</summary>
    /// <param name="left">The left row.</param>
    /// <param name="right">The right row.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static bool operator <(SqlRow<T1, T2, T3> left, SqlRow<T1, T2, T3> right) => throw Guidance.Exception();

    /// <summary>Row-value greater-than.</summary>
    /// <param name="left">The left row.</param>
    /// <param name="right">The right row.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static bool operator >(SqlRow<T1, T2, T3> left, SqlRow<T1, T2, T3> right) => throw Guidance.Exception();

    /// <summary>Row-value less-than-or-equal.</summary>
    /// <param name="left">The left row.</param>
    /// <param name="right">The right row.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static bool operator <=(SqlRow<T1, T2, T3> left, SqlRow<T1, T2, T3> right) => throw Guidance.Exception();

    /// <summary>Row-value greater-than-or-equal.</summary>
    /// <param name="left">The left row.</param>
    /// <param name="right">The right row.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static bool operator >=(SqlRow<T1, T2, T3> left, SqlRow<T1, T2, T3> right) => throw Guidance.Exception();
}
