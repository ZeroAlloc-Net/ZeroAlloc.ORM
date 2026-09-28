namespace ZeroAlloc.ORM.AotSmoke;

// Value-type column shapes for the AOT smoke, see ZeroAlloc-Net/ZeroAlloc.Cache#182:
// provider GetFieldValue<T> reads, enums in both storage modes, nullable primitives,
// a nullable Guid and a user struct, non-null and nullable.
public sealed record ValueRow(
    int Id,
    DateTimeOffset At,
    TimeSpan Duration,
    Guid Token,
    byte[] Payload,
    Priority Priority,
    Status Status,
    int? Score,
    Guid? Ref,
    Sku Sku,
    Sku? AltSku);
