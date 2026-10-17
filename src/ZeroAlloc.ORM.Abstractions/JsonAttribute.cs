namespace ZeroAlloc.ORM;

/// <summary>
/// Marks a string parameter, record constructor parameter or property as PostgreSQL
/// jsonb; the value is a JSON document held as a string.
/// </summary>
/// <remarks>
/// The generated binding sets the Npgsql parameter type to jsonb, so the SQL needs no
/// <c>::jsonb</c> cast. Reads are unchanged: a jsonb column reads as a string. The
/// marker needs Npgsql (ZAO082) and applies only to <see cref="string"/> (ZAO083).
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class JsonAttribute : Attribute;
