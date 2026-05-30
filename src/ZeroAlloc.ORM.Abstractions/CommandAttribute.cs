namespace ZeroAlloc.ORM;

/// <summary>
/// Marks a partial method as a SQL command (INSERT/UPDATE/DELETE or DDL).
/// <see cref="Kind"/> selects the execution mode: <see cref="CommandKind.NonQuery"/>
/// returns affected-row count, <see cref="CommandKind.Scalar"/> returns the first column
/// of the first row, <see cref="CommandKind.Identity"/> returns the generated identity.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CommandAttribute(string sql) : Attribute
{
    /// <summary>The SQL fragment, or — when <see cref="FromResource"/> is true — the embedded resource name.</summary>
    public string Sql { get; } = sql;

    /// <summary>
    /// When true, <see cref="Sql"/> names an embedded resource rather than inline SQL.
    /// Resource lookup happens at generator time.
    /// </summary>
    public bool FromResource { get; init; }

    /// <summary>Execution mode for the command. Defaults to <see cref="CommandKind.NonQuery"/>.</summary>
    public CommandKind Kind { get; init; } = CommandKind.NonQuery;
}
