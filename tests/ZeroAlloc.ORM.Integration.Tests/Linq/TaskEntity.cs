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
    public int? Priority { get; private set; }
    public float? Rating { get; private set; }
    public WorkKind? Category { get; private set; }
    public float Score { get; private set; }
    public Guid? ParentTaskId { get; private set; }

    // PostgreSQL's xmin, mapped by TaskMap as Daedalus maps its concurrency token.
    public uint RowVersion { get; private set; }
}
