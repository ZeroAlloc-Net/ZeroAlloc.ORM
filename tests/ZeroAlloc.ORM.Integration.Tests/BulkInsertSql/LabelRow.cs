namespace ZeroAlloc.ORM.Integration.Tests.BulkInsertSql;

// #326 — a BulkInsert row of reference-typed columns. Binding a string boxes
// nothing, so a hand-written loop can bind exactly what the emitted one binds
// and BulkInsertAllocationTests measures only what the chunk loop itself adds.
public sealed record LabelRow(string Code, string Label);
