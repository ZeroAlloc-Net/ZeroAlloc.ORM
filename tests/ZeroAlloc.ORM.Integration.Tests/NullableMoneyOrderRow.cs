namespace ZeroAlloc.ORM.Integration.Tests;

// v0.5 Phase C.3 — FlatRow whose composite ctor parameter is nullable. The
// generator emits the hoisted-local pattern for `Money? Total`: the per-column
// IsDBNull lookups decide between `null`, throw-on-mixed, and a materialized
// `new Money(...)`.
public sealed record NullableMoneyOrderRow(int Id, Money? Total);
