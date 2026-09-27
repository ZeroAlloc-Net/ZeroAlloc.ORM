using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// #255 — an InputOutput TimeSpan against a Postgres INOUT interval.
//
// Every stored-procedure output parameter declares a DbType from the tuple
// element's type (#235); TimeSpan maps to DbType.Time. For a pure OUT
// parameter that is harmless — no value is sent, and the read side already
// converts whatever Npgsql hands back (#245, #247). For INOUT, though, the
// generator writes the initial CLR value onto the parameter before execute,
// and a declared DbType.Time makes Npgsql encode that value as a Postgres
// `time`, which rejects 24 hours or more with "22008: time out of range".
//
// The fix leaves DbType unset for this one case (InputOutput + TimeSpan), so
// Npgsql infers NpgsqlDbType.Interval from the TimeSpan value itself, the
// same way an unannotated input parameter is inferred.
public sealed partial class PostgresIntervalInOutRepo(IAsyncDbConnection connection)
{
    [StoredProcedure("inout_interval_proc")]
    public partial Task<(TimeSpan Span, TimeSpan Other)> RoundTripAsync(
        [Param(Direction = System.Data.ParameterDirection.InputOutput)] TimeSpan span,
        [Param(Direction = System.Data.ParameterDirection.InputOutput)] TimeSpan other,
        CancellationToken ct);
}
