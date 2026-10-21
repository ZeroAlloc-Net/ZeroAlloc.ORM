namespace ZeroAlloc.ORM;

/// <summary>
/// Maps an entity type to a table. Applied to a map class that holds only attributes; the generator
/// reads it at build time.
/// </summary>
/// <param name="entity">The entity type being mapped.</param>
/// <param name="table">The table name.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TableMapAttribute(Type entity, string table) : Attribute
{
    /// <summary>Gets the entity type being mapped.</summary>
    public Type Entity { get; } = entity;

    /// <summary>Gets the table name.</summary>
    public string Table { get; } = table;

    /// <summary>Gets or sets the schema the table lives in, or null for the connection default.</summary>
    public string? Schema { get; init; }
}
