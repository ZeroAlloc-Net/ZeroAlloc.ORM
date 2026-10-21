namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// A domain-style entity, as Daedalus has them: private setters, never constructed by the ORM.
// LINQ companions only name its properties.
public sealed class ProjectEntity
{
    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public int Order { get; private set; }
}
