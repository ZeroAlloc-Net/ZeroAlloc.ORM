namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Daedalus' TaskExecution: one model call for a task, with its token counts. ExecutedAt is a
// DateTime, as Daedalus has it; Npgsql reads a timestamptz as a UTC DateTime.
public sealed class ExecutionEntity
{
    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public string? ModelId { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public DateTime ExecutedAt { get; private set; }
    public int? Sequence { get; private set; }
    public long? TotalTokens { get; private set; }
    public decimal? Cost { get; private set; }
    public float? Weight { get; private set; }
}
