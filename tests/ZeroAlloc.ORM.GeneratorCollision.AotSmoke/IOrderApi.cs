using ZeroAlloc.Rest.Attributes;

namespace ZeroAlloc.ORM.GeneratorCollision.AotSmoke;

// ZA.Rest-side of the collision smoke: a [ZeroAllocRestClient]-annotated
// interface. The ZA.Rest generator emits OrderApiClient as a sealed class
// implementing IOrderApi. We never invoke it over the network — the
// compile-time + AOT-publish guarantee is the signal we want.
//
// This file deliberately imports ZeroAlloc.Rest.Attributes (NOT ZeroAlloc.ORM)
// to keep the QueryAttribute names disambiguated by file-scoped usings.
[ZeroAllocRestClient]
public interface IOrderApi
{
    [Get("/orders/{id}")]
    Task<string?> GetOrderAsync(int id, CancellationToken ct = default);
}
