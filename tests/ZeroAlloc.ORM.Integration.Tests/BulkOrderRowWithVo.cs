namespace ZeroAlloc.ORM.Integration.Tests;

// v1.3 Task 9 — BulkInsert row whose CustomerId column is a [ValueObject]
// wrapper (CustomerId struct). The generator's per-row parameter binding
// unwraps `row.CustomerId.Value` on the way down to the DbParameter — the
// SingleArgCtor/ValueObject convention path that
// EmitBulkInsertCommand's BuildBulkInsertParameterValueExpression routes
// through. Distinct from BulkOrderRow which uses raw int.
public sealed record BulkOrderRowWithVo(CustomerId CustomerId, decimal Total);
