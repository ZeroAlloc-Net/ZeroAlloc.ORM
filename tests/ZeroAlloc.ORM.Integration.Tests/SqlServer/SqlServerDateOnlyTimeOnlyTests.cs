using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.SqlServer;

// #256 — DateOnly and TimeOnly on SQL Server 2022 through SqlClient, which binds
// them to `date` and `time` and reads them with GetFieldValue<T>.
public sealed class SqlServerDateOnlyTimeOnlyTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fx = new();

    public ValueTask InitializeAsync() => _fx.InitializeAsync();
    public ValueTask DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task DateOnly_and_TimeOnly_round_trip_through_parameters_rows_and_scalars()
    {
        await ExecuteAsync(
            "CREATE TABLE Shifts (Id INT PRIMARY KEY, ShiftDay DATE NOT NULL, StartTime TIME(7) NOT NULL, EndDay DATE NULL, EndTime TIME(7) NULL);");

        await ShiftAssertions.RoundTripAsync(_fx.Connection);
    }

    [Fact]
    public async Task Scalar_date_and_time_read_back_as_DateOnly_and_TimeOnly()
    {
        var repo = new SqlServerShiftProcedureRepo(_fx.Connection);

        Assert.Equal(new DateOnly(2024, 1, 2), await repo.ScalarDayAsync(CancellationToken.None));
        Assert.Equal(
            new TimeOnly(13, 14, 15).Add(TimeSpan.FromTicks(1_234_567)),
            await repo.ScalarClockAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Scalar_datetime2_with_a_time_of_day_does_not_read_as_DateOnly()
    {
        var repo = new SqlServerShiftProcedureRepo(_fx.Connection);

        await Assert.ThrowsAsync<InvalidCastException>(() => repo.ScalarDateTimeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task InputOutput_and_output_parameters_round_trip_as_DateOnly_and_TimeOnly()
    {
        await ExecuteAsync("""
            CREATE PROCEDURE dbo.shift_proc
                @day DATE OUTPUT,
                @start TIME(7) OUTPUT,
                @endDay DATE OUTPUT,
                @endTime TIME(7) OUTPUT
            AS
            BEGIN
                SET @day = DATEADD(DAY, 1, @day);
                SET @start = DATEADD(HOUR, 1, @start);
                SET @endDay = NULL;
                SET @endTime = '23:59:59.9999999';
            END
            """);

        var repo = new SqlServerShiftProcedureRepo(_fx.Connection);
        var (day, start, endDay, endTime) = await repo.ShiftAsync(
            ShiftAssertions.Day, ShiftAssertions.Start, null, null, CancellationToken.None);

        Assert.Equal(new DateOnly(2024, 1, 3), day);
        Assert.Equal(ShiftAssertions.Start.AddHours(1), start);
        Assert.Null(endDay);
        Assert.Equal(TimeOnly.MaxValue, endTime);
    }

    private async Task ExecuteAsync(string sql)
    {
        var cmd = _fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(default).ConfigureAwait(false);
        }
    }
}
