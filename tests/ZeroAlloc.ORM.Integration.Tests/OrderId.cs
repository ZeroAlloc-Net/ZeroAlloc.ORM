using ZeroAlloc.ValueObjects;

namespace ZeroAlloc.ORM.Integration.Tests;

// Identity-key wrapper around int. Used by CommandIdentityTests to exercise the
// VO branch of EmitShape.CommandIdentity — the generator unwraps the inner int
// from `__result` via Convert.ToInt32 then wraps in `new OrderId(...)` through
// the ConventionKind.SingleArgCtor path. Mirrors CustomerId's shape so the
// convention discovery is exercised consistently across the test surface.
[ValueObject]
public readonly partial struct OrderId
{
    public int Value { get; }
    public OrderId(int value) { Value = value; }
    public static OrderId From(int value) => new(value);
}
