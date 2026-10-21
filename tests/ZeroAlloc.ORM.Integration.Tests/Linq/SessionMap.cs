namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// "Sessions" in the default schema; every column is named after its property.
[TableMap(typeof(SessionEntity), "Sessions")]
internal static class SessionMap;
