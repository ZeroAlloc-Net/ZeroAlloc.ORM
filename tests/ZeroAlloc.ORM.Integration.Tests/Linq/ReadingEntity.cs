namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// A sensor reading with one DateTime column of each storage. TakenAt and CheckedAt are timestamp
// columns, RecordedAt and ClosedAt timestamptz ones, all declared in ReadingMap. SeenAt is a
// timestamptz its map declares nothing for, so its parameters keep the type Npgsql infers.
public sealed class ReadingEntity
{
    public Guid Id { get; private set; }
    public DateTime TakenAt { get; private set; }
    public DateTime RecordedAt { get; private set; }
    public DateTime SeenAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public DateTime? CheckedAt { get; private set; }
}
