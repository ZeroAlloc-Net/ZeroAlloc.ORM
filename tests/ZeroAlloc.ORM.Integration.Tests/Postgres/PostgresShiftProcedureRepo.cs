using System.Data;
using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// #256 — DateOnly and TimeOnly as INOUT and OUT parameters on Npgsql, declared as
// DbType.Date and DbType.Time. Npgsql returns `date` as DateOnly and `time` as
// TimeOnly, and a scalar command reads the same types.
//
// All-lowercase names because Postgres folds the procedure's unquoted parameter
// names; see StoredProcedureRepo for the full note.
public sealed partial class PostgresShiftProcedureRepo(IAsyncDbConnection connection)
{
    [StoredProcedure("shift_proc")]
    public partial Task<(DateOnly Day, TimeOnly Start, DateOnly? Endday, TimeOnly? Endtime)> ShiftAsync(
        [Param(Direction = ParameterDirection.InputOutput)] DateOnly day,
        [Param(Direction = ParameterDirection.InputOutput)] TimeOnly start,
        DateOnly? endday,
        TimeOnly? endtime,
        CancellationToken ct);

    [Command("SELECT DATE '2024-01-02'", Kind = CommandKind.Scalar)]
    public partial Task<DateOnly> ScalarDayAsync(CancellationToken ct);

    [Command("SELECT TIME '13:14:15.123456'", Kind = CommandKind.Scalar)]
    public partial Task<TimeOnly> ScalarClockAsync(CancellationToken ct);
}
