namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// "Tasks" in the default schema; every column is named after its property, and Kind is stored by name.
[TableMap(typeof(TaskEntity), "Tasks")]
[ColumnMap(nameof(TaskEntity.Kind), EnumAsString = true)]
internal static class TaskMap;
