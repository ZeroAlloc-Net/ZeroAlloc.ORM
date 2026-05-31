namespace ZeroAlloc.ORM.Integration.Tests;

// v0.5 Phase A.4 — canonical multi-column composite. Two ctor parameters
// (decimal Amount, string Currency) map to two SQL columns; the generator
// materializes `new Money(reader.GetDecimal(0), reader.GetString(1))`.
//
// Sqlite caveat: decimal stores as TEXT. Microsoft.Data.Sqlite's GetDecimal
// reads the TEXT and parses it under the current culture; for the in-range
// integer-valued decimals used by these tests the round-trip is lossless.
// A future `[Materialize(Factory = "FromStorage")]` recipe (v0.5 Phase D)
// covers the explicit string-parse path for adopters who can't tolerate the
// culture dependency.
public readonly record struct Money(decimal Amount, string Currency);
