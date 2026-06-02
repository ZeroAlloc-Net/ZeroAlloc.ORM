namespace ZeroAlloc.ORM.Integration.Tests;

// v1.3 Task 9 — BulkInsert row shape WITHOUT an Id column. The SQL's column
// list (CustomerId, Total) and the row's property set match exactly, so the
// placeholder parser binds `@CustomerId` -> property CustomerId,
// `@Total` -> property Total. Distinct from `OrderRow` which carries an
// explicit Id for FlatRow read scenarios.
public sealed record BulkOrderRow(int CustomerId, decimal Total);
