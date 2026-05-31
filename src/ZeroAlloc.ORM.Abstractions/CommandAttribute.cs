namespace ZeroAlloc.ORM;

/// <summary>
/// Marks a partial method as a SQL command (INSERT / UPDATE / DELETE, or a scalar
/// returning <c>ExecuteScalarAsync</c> result). The <see cref="Kind"/> value picks
/// between three emit shapes — see <see cref="CommandKind"/> and the v1.0 design
/// Section 2 for the dispatch table.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CommandAttribute(string sql) : Attribute
{
    /// <summary>The SQL fragment, or — when <see cref="FromResource"/> is true — the embedded resource name.</summary>
    public string Sql { get; } = sql;

    /// <summary>
    /// When true, <see cref="Sql"/> names an embedded resource (e.g. <c>"MyApp.Sql.InsertOrder"</c>)
    /// rather than inline SQL. Resource lookup happens at generator time. Reserved for a future
    /// milestone — v0.4 emits an informational diagnostic (ZAO020) when set, and the value is
    /// treated as literal inline SQL until the embedded-resource path ships. See
    /// <c>docs/diagnostics/ZAO020.md</c> for adopter guidance.
    /// </summary>
    public bool FromResource { get; init; }

    /// <summary>
    /// Picks the command emit shape. Default <see cref="CommandKind.NonQuery"/> matches
    /// adopter expectations for INSERT / UPDATE / DELETE statements.
    /// </summary>
    public CommandKind Kind { get; init; } = CommandKind.NonQuery;
}
