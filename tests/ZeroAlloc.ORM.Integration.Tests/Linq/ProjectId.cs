namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// The typed id of a project, a Guid underneath. Hand-written rather than [ValueObject]: the
// ZeroAlloc.ValueObjects 1.x generator emits Value.ToString(CultureInfo) for a single property,
// which does not compile for Guid. The ORM binds through Value and reads through From either way.
public readonly record struct ProjectId(Guid Value)
{
    public static ProjectId From(Guid value) => new(value);
}
