using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// #256 — DateOnly and TimeOnly on Sqlite. Microsoft.Data.Sqlite has no storage
// class for them and writes both as TEXT, `yyyy-MM-dd` and `HH:mm:ss.fffffff`.
// Its GetFieldValue<T> parses that text, and reads a DateOnly stored as a REAL or
// INTEGER Julian day. A scalar command gets the raw string or number from
// ExecuteScalar instead, and must read the same value.
public sealed class SqliteDateOnlyTimeOnlyTests
{
    private const string CreateTable =
        "CREATE TABLE Shifts (Id INTEGER PRIMARY KEY, ShiftDay TEXT NOT NULL, StartTime TEXT NOT NULL, EndDay TEXT NULL, EndTime TEXT NULL);";

    // No declared type, so a literal keeps its own storage class.
    private const string CreateUntypedTable =
        "CREATE TABLE Shifts (Id INTEGER PRIMARY KEY, ShiftDay NOT NULL, StartTime NOT NULL, EndDay NULL, EndTime NULL);";

    [Fact]
    public async Task DateOnly_and_TimeOnly_round_trip_through_parameters_rows_and_scalars()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(CreateTable).ConfigureAwait(false);

            await ShiftAssertions.RoundTripAsync(fx.Connection).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Parameters_store_the_provider_text_format()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(CreateTable).ConfigureAwait(false);
            var repo = new ShiftRepo(fx.Connection);
            await repo.InsertAsync(1, ShiftAssertions.Day, ShiftAssertions.Start, null, null, CancellationToken.None).ConfigureAwait(false);

            var cmd = fx.Connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = "SELECT ShiftDay || '|' || typeof(ShiftDay) || '|' || StartTime || '|' || typeof(StartTime) FROM Shifts";
                Assert.Equal(
                    "2024-01-02|text|13:14:15.1234560|text",
                    await cmd.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false));
            }
        }
    }

    [Theory]
    // TEXT as other tools write it: a date with a zero time, a time without a fraction.
    [InlineData("'2024-01-02T00:00:00'", "'13:14'")]
    [InlineData("'2024-01-02'", "'13:14:15'")]
    // A Julian day, REAL or INTEGER, as SQLite's julianday() stores a date.
    [InlineData("2460311.5", "'13:14:15.1234567'")]
    [InlineData("2460311.75", "'00:00:00'")]
    [InlineData("2460311", "'23:59:59.9999999'")]
    public async Task Scalar_reads_the_same_value_as_the_reader(string day, string time)
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(
                CreateUntypedTable + $"INSERT INTO Shifts VALUES (1, {day}, {time}, {day}, {time});").ConfigureAwait(false);
            var repo = new ShiftRepo(fx.Connection);

            var row = await repo.GetAsync(1, CancellationToken.None).ConfigureAwait(false);
            Assert.NotNull(row);
            Assert.Equal(row.ShiftDay, await repo.ShiftDayAsync(1, CancellationToken.None).ConfigureAwait(false));
            Assert.Equal(row.StartTime, await repo.StartTimeAsync(1, CancellationToken.None).ConfigureAwait(false));
            Assert.Equal(row.EndDay, await repo.EndDayAsync(1, CancellationToken.None).ConfigureAwait(false));
            Assert.Equal(row.EndTime, await repo.EndTimeAsync(1, CancellationToken.None).ConfigureAwait(false));
        }
    }

    [Fact]
    public async Task Julian_day_scalar_reads_the_calendar_date()
    {
        // Julian day 2460311.5 is 2024-01-02T00:00. A whole day number starts at
        // noon, so 2460311 is 2024-01-01T12:00.
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(
                CreateUntypedTable + "INSERT INTO Shifts VALUES (1, 2460311.5, '13:14', 2460311, NULL);").ConfigureAwait(false);
            var repo = new ShiftRepo(fx.Connection);

            Assert.Equal(new DateOnly(2024, 1, 2), await repo.ShiftDayAsync(1, CancellationToken.None).ConfigureAwait(false));
            Assert.Equal(new DateOnly(2024, 1, 1), await repo.EndDayAsync(1, CancellationToken.None).ConfigureAwait(false));
        }
    }
}
