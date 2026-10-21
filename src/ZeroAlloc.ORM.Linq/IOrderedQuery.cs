namespace ZeroAlloc.ORM.Linq;

/// <summary>Marker for a query chain that has been ordered, so that <c>ThenBy</c> applies.</summary>
/// <typeparam name="T">The element type of the query.</typeparam>
public interface IOrderedQuery<out T> : IQuery<T>;
