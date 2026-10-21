namespace ZeroAlloc.ORM.AotSmoke;

[TableMap(typeof(SmokeTask), "smoke_tasks", Schema = "public")]
[ColumnMap(nameof(SmokeTask.Id), Column = "id")]
[ColumnMap(nameof(SmokeTask.Title), Column = "title")]
internal static class SmokeTaskMap;
