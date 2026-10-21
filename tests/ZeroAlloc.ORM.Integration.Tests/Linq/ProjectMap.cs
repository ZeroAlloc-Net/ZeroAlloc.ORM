namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// "Projects" in schema app, with Title stored in the "Name" column.
[TableMap(typeof(ProjectEntity), "Projects", Schema = "app")]
[ColumnMap(nameof(ProjectEntity.Title), Column = "Name")]
internal static class ProjectMap;
