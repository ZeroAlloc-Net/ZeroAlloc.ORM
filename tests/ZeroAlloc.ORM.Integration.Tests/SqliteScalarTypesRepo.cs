using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// #257 — Sqlite has no DateTimeOffset, TimeSpan or Guid storage class. The
// generated parameters store all three as TEXT, an untyped Microsoft.Data.Sqlite
// parameter stores a Guid as a 16-byte BLOB, and ExecuteScalar hands back that
// string or byte array rather than the CLR type.
public sealed partial class SqliteScalarTypesRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO Typed (Id, Stamp, Span, Ident) VALUES (@id, @stamp, @span, @ident)")]
    public partial Task<int> InsertAsync(int id, DateTimeOffset stamp, TimeSpan span, Guid ident, CancellationToken ct);

    [Command("SELECT Stamp FROM Typed WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<DateTimeOffset> StampAsync(int id, CancellationToken ct);

    [Command("SELECT Span FROM Typed WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<TimeSpan> SpanAsync(int id, CancellationToken ct);

    [Command("SELECT Ident FROM Typed WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<Guid> IdentAsync(int id, CancellationToken ct);

    [Command("SELECT Stamp FROM Typed WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<DateTimeOffset?> NullableStampAsync(int id, CancellationToken ct);

    [Command("SELECT Span FROM Typed WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<TimeSpan?> NullableSpanAsync(int id, CancellationToken ct);

    [Command("SELECT Ident FROM Typed WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<Guid?> NullableIdentAsync(int id, CancellationToken ct);

    // #261 — Sqlite can also store dates as a Julian day number, REAL or INTEGER,
    // and a TimeSpan as a number of days. ExecuteScalar then returns a double or
    // a long.
    [Command("SELECT Stamp FROM NumericTyped WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<DateTimeOffset> NumericStampAsync(int id, CancellationToken ct);

    [Command("SELECT Moment FROM NumericTyped WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<DateTime> NumericMomentAsync(int id, CancellationToken ct);

    [Command("SELECT Span FROM NumericTyped WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<TimeSpan> NumericSpanAsync(int id, CancellationToken ct);

    [Command("SELECT Stamp FROM NumericTyped WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<DateTimeOffset?> NullableNumericStampAsync(int id, CancellationToken ct);

    [Command("SELECT Moment FROM NumericTyped WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<DateTime?> NullableNumericMomentAsync(int id, CancellationToken ct);

    [Command("SELECT Span FROM NumericTyped WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<TimeSpan?> NullableNumericSpanAsync(int id, CancellationToken ct);

    // #265 — dates stored as TEXT that the reader converts under Microsoft.Data.Sqlite
    // 10's time-zone handling: text without an offset is UTC for DateTimeOffset,
    // and text with an offset is converted to UTC for DateTime.
    [Command("SELECT Value FROM TextTemporal WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<DateTimeOffset> TextStampAsync(int id, CancellationToken ct);

    [Command("SELECT Value FROM TextTemporal WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<DateTime> TextMomentAsync(int id, CancellationToken ct);

    [Command("SELECT Value FROM TextTemporal WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<DateTimeOffset?> NullableTextStampAsync(int id, CancellationToken ct);

    [Command("SELECT Value FROM TextTemporal WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<DateTime?> NullableTextMomentAsync(int id, CancellationToken ct);
}
