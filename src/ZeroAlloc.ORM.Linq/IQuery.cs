namespace ZeroAlloc.ORM.Linq;

/// <summary>Marker for a query chain that the generator reads at build time. It has no members.</summary>
/// <typeparam name="T">The element type of the query.</typeparam>
public interface IQuery<out T>;
