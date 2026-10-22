namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// "Executions" in the default schema; every column is named after its property. ExecutedAt is a
// timestamptz, so its .Date is cut at the UTC day whatever the session's TimeZone. The setting is
// per column, not an assembly default, so other entities' DateTime columns render as before.
[TableMap(typeof(ExecutionEntity), "Executions")]
[ColumnMap(nameof(ExecutionEntity.ExecutedAt), DateTimeStorage = DateTimeStorage.TimestampTz)]
internal static class ExecutionMap;
