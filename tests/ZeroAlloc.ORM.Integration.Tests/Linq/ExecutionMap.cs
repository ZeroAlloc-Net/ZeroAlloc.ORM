namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// "Executions" in the default schema; every column is named after its property.
[TableMap(typeof(ExecutionEntity), "Executions")]
internal static class ExecutionMap;
