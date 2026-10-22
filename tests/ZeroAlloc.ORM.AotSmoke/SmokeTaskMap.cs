namespace ZeroAlloc.ORM.AotSmoke;

// Origin is owned: Url is renamed, Branch keeps EF's {Owner}_{Property} default "Origin_Branch".
[TableMap(typeof(SmokeTask), "smoke_tasks", Schema = "public")]
[OwnedMap(nameof(SmokeTask.Origin))]
[ColumnMap(nameof(SmokeTask.Id), Column = "id")]
[ColumnMap(nameof(SmokeTask.Title), Column = "title")]
[ColumnMap("Origin.Url", Column = "origin_url")]
internal static class SmokeTaskMap;
