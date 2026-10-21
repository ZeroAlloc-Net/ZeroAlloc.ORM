namespace ZeroAlloc.ORM.Linq;

/// <summary>One group produced by <c>GroupBy</c>, read by the generator at build time.</summary>
/// <typeparam name="TKey">The grouping key type.</typeparam>
/// <typeparam name="T">The element type of the group.</typeparam>
public interface IGroup<out TKey, out T>
{
    /// <summary>Gets the key shared by every row of the group.</summary>
    TKey Key { get; }
}
