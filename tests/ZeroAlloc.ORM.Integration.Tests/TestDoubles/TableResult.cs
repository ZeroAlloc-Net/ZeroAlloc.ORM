namespace ZeroAlloc.ORM.Integration.Tests.TestDoubles;

// #326 — one result set TableDataReader serves: column names and rows of cells,
// DBNull.Value for NULL.
internal sealed record TableResult(IReadOnlyList<string> Columns, IReadOnlyList<object[]> Rows);
