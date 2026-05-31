namespace ZeroAlloc.ORM.Integration.Tests;

// v0.4 Phase B.2 — single-arg record struct used by the CommandScalar
// value-object round-trip integration test. The generator's ConventionDiscovery
// resolves this as ConventionKind.SingleArgCtor and emits `new TotalAmount((decimal)__result!)`
// to wrap the unwrapped primitive returned from ExecuteScalarAsync.
public readonly partial record struct TotalAmount(decimal Value);
