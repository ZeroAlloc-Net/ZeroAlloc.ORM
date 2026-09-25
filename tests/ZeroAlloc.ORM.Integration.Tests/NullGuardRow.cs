namespace ZeroAlloc.ORM.Integration.Tests;

// #249 — a positional record whose every column is non-nullable: a string, an int,
// a default-int enum and an int-backed value object. A NULL in any of them must
// throw ZeroAllocOrmMaterializationException naming the column and the parameter.
public sealed record NullGuardRow(int Id, string Name, int Quantity, Status State, OrderId Ref);
