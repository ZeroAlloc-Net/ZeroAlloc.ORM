namespace ZeroAlloc.ORM;

/// <summary>
/// Declares an entity property an owned type, as EF Core's <c>OwnsOne</c> does: the owned object has
/// no table of its own, and each of its public readable instance properties that holds a value is a
/// column of the entity's table. Applied next to <see cref="TableMapAttribute"/> on the map class,
/// once for each owned property.
/// </summary>
/// <remarks>
/// <para>
/// A member's column is named after EF Core's convention, <c>{Owner}_{Property}</c>, such as
/// <c>Repository_Url</c>. A <see cref="ColumnMapAttribute"/> with a dotted path overrides it and
/// sets the other storage options for the member, as for a plain column:
/// <c>[ColumnMap("Repository.Url", Column = "RepositoryUrl")]</c>.
/// </para>
/// <para>
/// A LINQ query reads a member of the owned object, such as <c>r.Repository.Url</c>, as its column.
/// An optional owned object whose columns are all NULL reads its members as NULL, where C# would
/// throw on the null owner.
/// </para>
/// </remarks>
/// <param name="property">The entity property that holds the owned object.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class OwnedMapAttribute(string property) : Attribute
{
    /// <summary>Gets the entity property that holds the owned object.</summary>
    public string Property { get; } = property;
}
