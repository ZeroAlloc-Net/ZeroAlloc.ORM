namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// "Readings" in the default schema. SeenAt declares no storage.
[TableMap(typeof(ReadingEntity), "Readings")]
[ColumnMap(nameof(ReadingEntity.TakenAt), DateTimeStorage = DateTimeStorage.Timestamp)]
[ColumnMap(nameof(ReadingEntity.CheckedAt), DateTimeStorage = DateTimeStorage.Timestamp)]
[ColumnMap(nameof(ReadingEntity.RecordedAt), DateTimeStorage = DateTimeStorage.TimestampTz)]
[ColumnMap(nameof(ReadingEntity.ClosedAt), DateTimeStorage = DateTimeStorage.TimestampTz)]
internal static class ReadingMap;
