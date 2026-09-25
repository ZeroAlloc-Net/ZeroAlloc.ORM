using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// #245, #247 — temporal stored-procedure outputs on Npgsql.
//
// Npgsql fills an OUT parameter with the column's default CLR type, which is not
// always the tuple element's type: `timestamptz` comes back as a UTC DateTime,
// `time` as TimeOnly and `date` as DateOnly. The reader path asks for the target
// type through GetFieldValue<T>; the output readback converts the boxed value to
// the same result.
//
// All-lowercase names because Postgres folds the procedure's unquoted parameter
// names; see StoredProcedureRepo for the full note.
public sealed partial class PostgresTemporalOutputRepo(IAsyncDbConnection connection)
{
    [StoredProcedure("stamp_proc")]
    public partial Task<(DateTimeOffset Stamp, DateTimeOffset? Missing)> StampAsync(
        DateTimeOffset stamp,
        DateTimeOffset? missing,
        CancellationToken ct);

    [StoredProcedure("clock_proc")]
    public partial Task<(TimeSpan Clock, TimeSpan? Late, TimeSpan? Absent)> ClockAsync(
        TimeSpan clock,
        TimeSpan? late,
        TimeSpan? absent,
        CancellationToken ct);

    [StoredProcedure("interval_proc")]
    public partial Task<(TimeSpan Brief, TimeSpan Lengthy)> IntervalAsync(
        TimeSpan brief,
        TimeSpan lengthy,
        CancellationToken ct);

    [StoredProcedure("dates_proc")]
    public partial Task<(DateTime Day, DateTime Plain, DateTime Utc)> DatesAsync(
        DateTime day,
        DateTime plain,
        DateTime utc,
        CancellationToken ct);

    // ExecuteScalar hands back the same default CLR type, so a scalar command
    // over a timestamptz or time value goes through the same conversion.
    [Command("SELECT TIMESTAMPTZ '2024-01-02 03:04:05.123456+02'", Kind = CommandKind.Scalar)]
    public partial Task<DateTimeOffset> ScalarStampAsync(CancellationToken ct);

    [Command("SELECT TIME '13:14:15.123456'", Kind = CommandKind.Scalar)]
    public partial Task<TimeSpan> ScalarClockAsync(CancellationToken ct);

    [Command("SELECT DATE '2024-01-02'", Kind = CommandKind.Scalar)]
    public partial Task<DateTime> ScalarDayAsync(CancellationToken ct);

    // A nullable scalar keeps the NULL short-circuit in front of the conversion.
    [Command("SELECT CASE WHEN @present THEN TIMESTAMPTZ '2024-01-02 03:04:05.123456+02' END", Kind = CommandKind.Scalar)]
    public partial Task<DateTimeOffset?> ScalarMaybeStampAsync(bool present, CancellationToken ct);

    [Command("SELECT CASE WHEN @present THEN TIME '13:14:15.123456' END", Kind = CommandKind.Scalar)]
    public partial Task<TimeSpan?> ScalarMaybeClockAsync(bool present, CancellationToken ct);
}
