namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// A domain-style entity, as Daedalus has them: private setters, never constructed by the ORM.
// LINQ companions only name its properties.
public sealed class TaskEntity
{
    public Guid Id { get; private set; }
    public ProjectId ProjectId { get; private set; }
    public string Title { get; private set; } = "";
    public WorkStatus Status { get; private set; }
    public WorkKind Kind { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DueAt { get; private set; }
    public string[] Tags { get; private set; } = [];
    public int Order { get; private set; }
    public DateTime? StartedAt { get; private set; }
}
