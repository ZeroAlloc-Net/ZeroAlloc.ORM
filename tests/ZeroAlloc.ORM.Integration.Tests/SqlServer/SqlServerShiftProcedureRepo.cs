using System.Data;
using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests.SqlServer;

// #256 — DateOnly and TimeOnly as InputOutput and Output parameters on SqlClient,
// declared as DbType.Date and DbType.Time. SqlClient accepts DateOnly and
// TimeOnly values but hands a `date` back as DateTime and a `time` as TimeSpan,
// in an output parameter and from ExecuteScalar alike.
public sealed partial class SqlServerShiftProcedureRepo(IAsyncDbConnection connection)
{
    [StoredProcedure("dbo.shift_proc")]
    public partial Task<(DateOnly Day, TimeOnly Start, DateOnly? EndDay, TimeOnly? EndTime)> ShiftAsync(
        [Param(Direction = ParameterDirection.InputOutput)] DateOnly day,
        [Param(Direction = ParameterDirection.InputOutput)] TimeOnly start,
        DateOnly? endDay,
        TimeOnly? endTime,
        CancellationToken ct);

    [Command("SELECT CAST('2024-01-02' AS DATE)", Kind = CommandKind.Scalar)]
    public partial Task<DateOnly> ScalarDayAsync(CancellationToken ct);

    [Command("SELECT CAST('13:14:15.1234567' AS TIME(7))", Kind = CommandKind.Scalar)]
    public partial Task<TimeOnly> ScalarClockAsync(CancellationToken ct);

    // A datetime2 with a time of day is not a date: the scalar throws rather
    // than drop the time.
    [Command("SELECT CAST('2024-01-02T03:04:05' AS DATETIME2)", Kind = CommandKind.Scalar)]
    public partial Task<DateOnly> ScalarDateTimeAsync(CancellationToken ct);
}
