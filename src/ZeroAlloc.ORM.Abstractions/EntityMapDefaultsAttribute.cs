namespace ZeroAlloc.ORM;

/// <summary>
/// Defaults for every entity map in the assembly:
/// <c>[assembly: EntityMapDefaults(DateTimeStorage = DateTimeStorage.TimestampTz)]</c>. A
/// <see cref="ColumnMapAttribute"/> setting for one column wins over it.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class EntityMapDefaultsAttribute : Attribute
{
    /// <summary>
    /// Gets or sets how a <see cref="System.DateTime"/> column is stored when its
    /// <see cref="ColumnMapAttribute.DateTimeStorage"/> is <see cref="ORM.DateTimeStorage.Default"/>.
    /// </summary>
    public DateTimeStorage DateTimeStorage { get; init; }
}
