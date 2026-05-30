namespace ZeroAlloc.ORM.Integration.Tests;

// Positional record with a value-object column — proves that EmitShape.FlatRow
// composes with per-column ValueObject materialization (CustomerId.From(int)).
public sealed record CustomerRow(CustomerId Id, string Name);
