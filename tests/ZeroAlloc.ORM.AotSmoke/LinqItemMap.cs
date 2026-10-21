namespace ZeroAlloc.ORM.AotSmoke;

// public.linq_items with snake_case columns. Priority has no [StoreAsString], so EnumAsString
// stores it by name here; Status is string-stored by its own attribute.
[TableMap(typeof(LinqItem), "linq_items", Schema = "public")]
[ColumnMap(nameof(LinqItem.Id), Column = "id")]
[ColumnMap(nameof(LinqItem.Name), Column = "name")]
[ColumnMap(nameof(LinqItem.Rank), Column = "rank")]
[ColumnMap(nameof(LinqItem.Priority), Column = "priority", EnumAsString = true)]
[ColumnMap(nameof(LinqItem.Status), Column = "status")]
internal static class LinqItemMap;
