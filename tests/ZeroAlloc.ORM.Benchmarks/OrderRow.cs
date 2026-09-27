namespace ZeroAlloc.ORM.Benchmarks;

// Row shape shared across every benchmark in the assembly (Sqlite + Postgres,
// single-row + multi-row + multi-result-set). One type per file to satisfy
// MA0048.

public sealed record OrderRow(int Id, int CustomerId, decimal Total);
