namespace ZeroAlloc.ORM.Benchmarks;

// Detail-row shape used by the multi-result-set benchmarks (Sqlite + Postgres).
// One type per file to satisfy MA0048.

public sealed record OrderLineRow(int Id, int OrderId, string Sku, int Qty);
