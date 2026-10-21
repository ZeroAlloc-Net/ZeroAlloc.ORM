namespace ZeroAlloc.ORM.AotSmoke;

[TableMap(typeof(SmokeRun), "smoke_runs", Schema = "public")]
[ColumnMap(nameof(SmokeRun.Id), Column = "id")]
[ColumnMap(nameof(SmokeRun.TaskId), Column = "task_id")]
[ColumnMap(nameof(SmokeRun.Tokens), Column = "tokens")]
internal static class SmokeRunMap;
