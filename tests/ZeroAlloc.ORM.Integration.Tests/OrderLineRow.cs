namespace ZeroAlloc.ORM.Integration.Tests;

// Companion row to OrderRow for the v0.3 Phase E MultiResultSet integration suite —
// the "lines" half of the canonical (Head, Lines) head + lines pattern. Lives in its
// own file to mirror the rest of the integration repo (one row type per file).
public sealed record OrderLineRow(int Id, int OrderId, string Sku, int Qty);
