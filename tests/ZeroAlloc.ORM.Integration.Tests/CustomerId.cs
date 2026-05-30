using ZeroAlloc.ValueObjects;

namespace ZeroAlloc.ORM.Integration.Tests;

// Single-value wrapper around int. The [ValueObject] attribute (from
// ZA.ValueObjects) drives the zero-alloc equality emit; the Value property
// and static From factory are hand-rolled because ZA.ValueObjects only emits
// equality members. ZA.ORM's ConventionDiscovery binds parameters by reading
// Value and materializes by calling From — exercises EmitShape.FlatRow with a
// per-column ValueObject convention.
[ValueObject]
public readonly partial struct CustomerId
{
    public int Value { get; }
    public CustomerId(int value) { Value = value; }
    public static CustomerId From(int value) => new(value);
}
