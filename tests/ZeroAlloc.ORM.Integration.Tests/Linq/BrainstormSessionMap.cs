namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// "BrainstormSessions" in the default schema; every column is named after its property.
[TableMap(typeof(BrainstormSessionEntity), "BrainstormSessions")]
internal static class BrainstormSessionMap;
