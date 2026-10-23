namespace ZeroAlloc.ORM.Linq;

/// <summary>
/// The setters of an <see cref="QueryOperators.ExecuteUpdate{T}"/>, one <c>SET</c> item per
/// <c>SetProperty</c>, in written order. Setters written as EF Core's <c>ExecuteUpdate</c> takes
/// them, <c>set =&gt; set.SetProperty(...)</c>, keep compiling against it. The generator reads
/// the calls at build time; none of them run.
/// </summary>
/// <typeparam name="T">The row type of the updated table.</typeparam>
public sealed class SetPropertyCalls<T>
{
    private SetPropertyCalls()
    {
    }

    /// <summary>
    /// Sets a column to a value: a companion parameter or a constant, <c>null</c> included for a
    /// column that can hold it. An enum binds the way its column stores it.
    /// </summary>
    /// <typeparam name="TProperty">The column's type.</typeparam>
    /// <param name="property">The column, a mapped member of the row such as <c>t =&gt; t.State</c>.</param>
    /// <param name="value">The value.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public SetPropertyCalls<T> SetProperty<TProperty>(Func<T, TProperty> property, TProperty value) => throw Guidance.Exception();

    /// <summary>
    /// Sets a column to a value computed from the row: a column of the same row, a companion
    /// parameter or a constant, or <c>+</c>, <c>-</c> and <c>*</c> over those on <c>int</c>,
    /// <c>long</c>, <c>decimal</c>, <c>double</c> and <c>float</c>, such as
    /// <c>t =&gt; t.TurnCount + 1</c>. PostgreSQL computes it in the statement, under the row's
    /// lock, so concurrent increments add up.
    /// </summary>
    /// <remarks>
    /// An integer result outside its type raises PostgreSQL's "out of range" error, where C# in
    /// an unchecked context would wrap around.
    /// </remarks>
    /// <typeparam name="TProperty">The column's type.</typeparam>
    /// <param name="property">The column, a mapped member of the row such as <c>t =&gt; t.TurnCount</c>.</param>
    /// <param name="value">The value, computed from the row.</param>
    /// <returns>Never returns; the generator reads the call at build time.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public SetPropertyCalls<T> SetProperty<TProperty>(Func<T, TProperty> property, Func<T, TProperty> value) => throw Guidance.Exception();
}
