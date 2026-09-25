namespace ZeroAlloc.ORM.Integration.Tests;

// #249 — the nullable control for NullGuardRow. The same columns declared
// nullable read a NULL back as null instead of throwing.
public sealed record NullableGuardRow(int Id, string? Name, int? Quantity, Status? State, OrderId? Ref);
