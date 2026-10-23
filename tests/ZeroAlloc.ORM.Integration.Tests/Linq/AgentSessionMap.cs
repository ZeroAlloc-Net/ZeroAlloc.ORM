namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// "AgentSessions" in the default schema; State is stored by name.
[TableMap(typeof(AgentSessionEntity), "AgentSessions")]
[ColumnMap(nameof(AgentSessionEntity.State), EnumAsString = true)]
internal static class AgentSessionMap;
