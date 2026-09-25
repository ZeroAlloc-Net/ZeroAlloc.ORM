namespace ZeroAlloc.ORM.Integration.Tests;

// #249 — see CheckedQuantity.
public sealed record CheckedQuantityRow(int Id, string? Name, CheckedQuantity Quantity);
