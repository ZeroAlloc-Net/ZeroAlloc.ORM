using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// #256 — DateOnly and TimeOnly on Npgsql, which maps them natively to `date` and
// `time`.
[Trait("Provider", "Postgres")]
public sealed class PostgresDateOnlyTimeOnlyTests
{
    [Fact]
    public async Task DateOnly_and_TimeOnly_round_trip_through_parameters_rows_and_scalars()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(
            "CREATE TABLE Shifts (Id INT PRIMARY KEY, ShiftDay DATE NOT NULL, StartTime TIME NOT NULL, EndDay DATE NULL, EndTime TIME NULL);")
            .ConfigureAwait(false);

        await ShiftAssertions.RoundTripAsync(fx.Connection).ConfigureAwait(false);
    }

    [Fact]
    public async Task Scalar_date_and_time_read_back_as_DateOnly_and_TimeOnly()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        var repo = new PostgresShiftProcedureRepo(fx.Connection);

        Assert.Equal(new DateOnly(2024, 1, 2), await repo.ScalarDayAsync(CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(ShiftAssertions.Start, await repo.ScalarClockAsync(CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public async Task InOut_and_out_parameters_round_trip_as_DateOnly_and_TimeOnly()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE PROCEDURE shift_proc(INOUT day date, INOUT start time, OUT endday date, OUT endtime time)
                LANGUAGE plpgsql
            AS $$
            BEGIN
                day := day + 1;
                start := start + INTERVAL '1 hour';
                endday := NULL;
                endtime := '23:59:59.999999';
            END;
            $$;").ConfigureAwait(false);

        var repo = new PostgresShiftProcedureRepo(fx.Connection);
        var (day, start, endday, endtime) = await repo.ShiftAsync(
            ShiftAssertions.Day, ShiftAssertions.Start, null, null, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(new DateOnly(2024, 1, 3), day);
        Assert.Equal(ShiftAssertions.Start.AddHours(1), start);
        Assert.Null(endday);
        Assert.Equal(ShiftAssertions.Late, endtime);
    }
}
