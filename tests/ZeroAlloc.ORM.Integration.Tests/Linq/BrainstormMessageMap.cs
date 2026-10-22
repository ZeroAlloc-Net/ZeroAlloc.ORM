namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// "BrainstormMessages" in the default schema; every column is named after its property.
[TableMap(typeof(BrainstormMessageEntity), "BrainstormMessages")]
internal static class BrainstormMessageMap;
