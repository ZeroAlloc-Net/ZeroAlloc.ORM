namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// "Tasks" in the default schema; every column is named after its property, Kind is stored by name,
// and RowVersion is the xmin system column, as Daedalus maps it.
[TableMap(typeof(TaskEntity), "Tasks")]
[ColumnMap(nameof(TaskEntity.Kind), EnumAsString = true)]
[ColumnMap(nameof(TaskEntity.RowVersion), Column = "xmin")]
internal static class TaskMap;
