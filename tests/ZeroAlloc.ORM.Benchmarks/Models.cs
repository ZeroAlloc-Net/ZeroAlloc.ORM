namespace ZeroAlloc.ORM.Benchmarks;

// Row shapes shared across every benchmark in the assembly (Sqlite + Postgres,
// single-row + multi-row + multi-result-set). Extracted to a dedicated file to
// satisfy MA0048 (one public type per file) and to keep individual benchmark
// files focused on their workload rather than DTOs.

public sealed record OrderRow(int Id, int CustomerId, decimal Total);

public sealed record OrderLineRow(int Id, int OrderId, string Sku, int Qty);
