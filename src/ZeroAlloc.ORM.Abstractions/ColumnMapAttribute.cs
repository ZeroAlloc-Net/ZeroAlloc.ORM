namespace ZeroAlloc.ORM;

/// <summary>
/// Overrides how one property of a mapped entity is stored. Repeat it for each property that needs an
/// override; applied next to <see cref="TableMapAttribute"/> on the map class.
/// </summary>
/// <param name="property">The entity property name.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ColumnMapAttribute(string property) : Attribute
{
    /// <summary>Gets the entity property name.</summary>
    public string Property { get; } = property;

    /// <summary>Gets or sets the column name, or null to use the default naming.</summary>
    public string? Column { get; init; }

    /// <summary>Gets or sets a value indicating whether an enum property is stored as its name.</summary>
    public bool EnumAsString { get; init; }
}
