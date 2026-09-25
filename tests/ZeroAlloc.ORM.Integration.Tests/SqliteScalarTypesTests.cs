using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// #257 — scalar DateTimeOffset, TimeSpan and Guid on Sqlite. Each value must read
// back as the one written, and as the value GetFieldValue<T> reads for the same
// column, so a scalar command and a row query agree.
public sealed class SqliteScalarTypesTests
{
    private static readonly DateTimeOffset Stamp =
        new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)).AddTicks(1_234_567);

    private static readonly TimeSpan Span = new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(5_678_901));

    private static readonly Guid Ident = new("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    [Fact]
    public async Task Values_written_through_parameters_read_back_as_scalars()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await CreateTableAsync(fx).ConfigureAwait(false);
            var repo = new SqliteScalarTypesRepo(fx.Connection);
            await repo.InsertAsync(1, Stamp, Span, Ident, CancellationToken.None).ConfigureAwait(false);

            var stamp = await repo.StampAsync(1, CancellationToken.None).ConfigureAwait(false);
            Assert.Equal(Stamp, stamp);
            Assert.Equal(Stamp.Offset, stamp.Offset);
            Assert.Equal(Span, await repo.SpanAsync(1, CancellationToken.None).ConfigureAwait(false));
            Assert.Equal(Ident, await repo.IdentAsync(1, CancellationToken.None).ConfigureAwait(false));

            Assert.Equal(Stamp, await repo.NullableStampAsync(1, CancellationToken.None).ConfigureAwait(false));
            Assert.Equal(Span, await repo.NullableSpanAsync(1, CancellationToken.None).ConfigureAwait(false));
            Assert.Equal(Ident, await repo.NullableIdentAsync(1, CancellationToken.None).ConfigureAwait(false));

            await AssertMatchesReaderAsync(fx, repo, 1).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Text_values_written_by_other_tools_read_back_as_scalars()
    {
        // Literal TEXT in the formats Microsoft.Data.Sqlite writes, with an
        // upper-case Guid as other tools commonly write it.
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await CreateTableAsync(fx).ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                INSERT INTO Typed (Id, Stamp, Span, Ident) VALUES
                    (2, '2024-01-02 03:04:05.1234567+02:00', '1.02:03:04.5678901',
                     '6F9619FF-8B86-D011-B42D-00C04FC964FF');").ConfigureAwait(false);
            var repo = new SqliteScalarTypesRepo(fx.Connection);

            Assert.Equal(Stamp, await repo.StampAsync(2, CancellationToken.None).ConfigureAwait(false));
            Assert.Equal(Span, await repo.SpanAsync(2, CancellationToken.None).ConfigureAwait(false));
            Assert.Equal(Ident, await repo.IdentAsync(2, CancellationToken.None).ConfigureAwait(false));

            await AssertMatchesReaderAsync(fx, repo, 2).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Guid_stored_as_a_blob_reads_back_as_a_scalar()
    {
        // An untyped SqliteParameter binds a Guid as its 16 bytes, so a table
        // written through plain ADO.NET holds BLOB Guids.
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await CreateTableAsync(fx).ConfigureAwait(false);
            await fx.ExecuteDdlAsync(
                "INSERT INTO Typed (Id, Stamp, Span, Ident) VALUES (4, '2024-01-02 03:04:05.1234567+02:00', " +
                "'1.02:03:04.5678901', X'" + Convert.ToHexString(Ident.ToByteArray()) + "');").ConfigureAwait(false);
            var repo = new SqliteScalarTypesRepo(fx.Connection);

            Assert.Equal(Ident, await repo.IdentAsync(4, CancellationToken.None).ConfigureAwait(false));
            Assert.Equal(Ident, await repo.NullableIdentAsync(4, CancellationToken.None).ConfigureAwait(false));

            await AssertMatchesReaderAsync(fx, repo, 4).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Guid_stored_as_a_text_blob_reads_back_as_a_scalar()
    {
        // A BLOB that is not 16 bytes holds the Guid's text as UTF-8, which
        // Microsoft.Data.Sqlite's GetGuid also accepts.
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await CreateTableAsync(fx).ConfigureAwait(false);
            await fx.ExecuteDdlAsync(
                "INSERT INTO Typed (Id, Stamp, Span, Ident) VALUES (5, '2024-01-02 03:04:05.1234567+02:00', " +
                "'1.02:03:04.5678901', CAST('6f9619ff-8b86-d011-b42d-00c04fc964ff' AS BLOB));").ConfigureAwait(false);
            var repo = new SqliteScalarTypesRepo(fx.Connection);

            Assert.Equal(Ident, await repo.IdentAsync(5, CancellationToken.None).ConfigureAwait(false));
            Assert.Equal(Ident, await repo.NullableIdentAsync(5, CancellationToken.None).ConfigureAwait(false));

            await AssertMatchesReaderAsync(fx, repo, 5).ConfigureAwait(false);
        }
    }

    // Microsoft.Data.Sqlite writes and reads these values with InvariantCulture,
    // so the scalar conversion must parse them the same way whatever the current
    // culture is. Each value is also written under the culture, as an adopter's
    // app would.
    //   * nl-NL uses a comma as its decimal separator; the stored fractional
    //     seconds use a period.
    //   * th-TH defaults to the Thai Buddhist calendar, so a current-culture
    //     DateTimeOffset.Parse reads the year 2024 as 2024 BE, which is 1481 AD.
    //     This is the case that fails if the conversion drops InvariantCulture.
    [Theory]
    [InlineData("nl-NL")]
    [InlineData("th-TH")]
    public async Task Text_values_parse_the_same_under_another_culture(string culture)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
        try
        {
            var fx = new SqliteFixture();
            await using (fx.ConfigureAwait(false))
            {
                await fx.InitializeAsync().ConfigureAwait(false);
                await CreateTableAsync(fx).ConfigureAwait(false);
                var repo = new SqliteScalarTypesRepo(fx.Connection);
                await repo.InsertAsync(6, Stamp, Span, Ident, CancellationToken.None).ConfigureAwait(false);
                await fx.ExecuteDdlAsync(@"
                    INSERT INTO Typed (Id, Stamp, Span, Ident) VALUES
                        (7, '2024-01-02 03:04:05.1234567+02:00', '1.02:03:04.5678901',
                         '6F9619FF-8B86-D011-B42D-00C04FC964FF');").ConfigureAwait(false);

                foreach (var id in new[] { 6, 7 })
                {
                    var stamp = await repo.StampAsync(id, CancellationToken.None).ConfigureAwait(false);
                    Assert.Equal(Stamp, stamp);
                    Assert.Equal(Stamp.Offset, stamp.Offset);
                    Assert.Equal(Span, await repo.SpanAsync(id, CancellationToken.None).ConfigureAwait(false));
                    Assert.Equal(Ident, await repo.IdentAsync(id, CancellationToken.None).ConfigureAwait(false));
                    await AssertMatchesReaderAsync(fx, repo, id).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Null_values_read_back_as_null()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await CreateTableAsync(fx).ConfigureAwait(false);
            await fx.ExecuteDdlAsync("INSERT INTO Typed (Id) VALUES (3);").ConfigureAwait(false);
            var repo = new SqliteScalarTypesRepo(fx.Connection);

            Assert.Null(await repo.NullableStampAsync(3, CancellationToken.None).ConfigureAwait(false));
            Assert.Null(await repo.NullableSpanAsync(3, CancellationToken.None).ConfigureAwait(false));
            Assert.Null(await repo.NullableIdentAsync(3, CancellationToken.None).ConfigureAwait(false));
        }
    }

    // #261 — numeric storage. Microsoft.Data.Sqlite reads a REAL or INTEGER date
    // as a Julian day number, and a REAL or INTEGER TimeSpan as a number of days.
    // Each row mixes both storage classes; the expected values are what SQLite
    // itself and GetFieldValue<T> make of them.
    //   * 1: julianday() of a date with milliseconds, and 1.5 days, both REAL.
    //   * 2: whole Julian days and a whole number of days, both INTEGER. Julian
    //        days start at noon, so an INTEGER date reads as 12:00.
    //   * 3: a REAL Julian day whose fraction needs rounding to the millisecond,
    //        and a fractional number of days.
    [Theory]
    [InlineData(1, "2024-01-02T03:04:05.1230000", "1.12:00:00")]
    [InlineData(2, "2024-01-01T12:00:00.0000000", "2.00:00:00")]
    [InlineData(3, "2000-01-01T00:00:00.0010000", "0.06:00:00")]
    public async Task Numeric_values_read_back_as_scalars(int id, string moment, string span)
    {
        var expectedMoment = DateTime.Parse(moment, System.Globalization.CultureInfo.InvariantCulture);
        var expectedSpan = TimeSpan.Parse(span, System.Globalization.CultureInfo.InvariantCulture);
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await CreateNumericTableAsync(fx).ConfigureAwait(false);
            var repo = new SqliteScalarTypesRepo(fx.Connection);

            var stamp = await repo.NumericStampAsync(id, CancellationToken.None).ConfigureAwait(false);
            Assert.Equal(new DateTimeOffset(expectedMoment, TimeSpan.Zero), stamp);
            Assert.Equal(TimeSpan.Zero, stamp.Offset);
            var dateTime = await repo.NumericMomentAsync(id, CancellationToken.None).ConfigureAwait(false);
            Assert.Equal(expectedMoment, dateTime);
            Assert.Equal(DateTimeKind.Unspecified, dateTime.Kind);
            Assert.Equal(expectedSpan, await repo.NumericSpanAsync(id, CancellationToken.None).ConfigureAwait(false));

            Assert.Equal(stamp, await repo.NullableNumericStampAsync(id, CancellationToken.None).ConfigureAwait(false));
            Assert.Equal(expectedMoment, await repo.NullableNumericMomentAsync(id, CancellationToken.None).ConfigureAwait(false));
            Assert.Equal(expectedSpan, await repo.NullableNumericSpanAsync(id, CancellationToken.None).ConfigureAwait(false));

            await AssertNumericMatchesReaderAsync(fx, repo, id).ConfigureAwait(false);
        }
    }

    // An INTEGER is a Julian day, not Unix seconds. Read as a Julian day, a Unix
    // timestamp is far past year 9999, so the reader and the scalar both throw
    // rather than return a wrong date.
    [Fact]
    public async Task Unix_seconds_are_not_a_valid_numeric_date_on_either_path()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await CreateNumericTableAsync(fx).ConfigureAwait(false);
            await fx.ExecuteDdlAsync(
                "INSERT INTO NumericTyped (Id, Stamp, Moment, Span) VALUES (9, 1700000000, 1700000000, 0);").ConfigureAwait(false);
            var repo = new SqliteScalarTypesRepo(fx.Connection);

            var cmd = fx.Connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = "SELECT Moment FROM NumericTyped WHERE Id = 9";
                var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                await using (((System.IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
                {
                    Assert.True(await reader.ReadAsync().ConfigureAwait(false));
                    Assert.NotNull(Record.Exception(() => reader.GetFieldValue<DateTime>(0)));
                }
            }

            Assert.NotNull(await Record.ExceptionAsync(() => repo.NumericMomentAsync(9, CancellationToken.None)).ConfigureAwait(false));
            Assert.NotNull(await Record.ExceptionAsync(() => repo.NumericStampAsync(9, CancellationToken.None)).ConfigureAwait(false));
        }
    }

    // #265 — time-zone handling of TEXT dates, as Microsoft.Data.Sqlite 10 reads
    // them without its Pre10TimeZoneHandling switch:
    //   * DateTimeOffset: text without an offset is taken as UTC, offset zero.
    //     Text with an offset keeps it.
    //   * DateTime: text with an offset, or a trailing Z, is converted to UTC
    //     with Kind Utc. Text without one is read as written, Kind Unspecified.
    // The expected values are independent of the machine's time zone. On a
    // machine at UTC the old conversion gave the same instants: the DateTime
    // rows with an offset or a Z still fail there on their Kind check, Local
    // instead of Utc. The DateTimeOffset rows without an offset cannot fail on a
    // UTC runner, because the local offset is zero there. The machine-independent
    // guard for that case is the snapshot test
    // StoredProcedureOutputParamsEmitTests
    //     .SprocWithOutputParams_temporal_outputs_convert_from_provider_default_types,
    // which pins DateTimeStyles.AssumeUniversal in the generated conversion.
    [Theory]
    [InlineData("2024-01-02 03:04:05", "2024-01-02T03:04:05.0000000+00:00", "2024-01-02T03:04:05.0000000", DateTimeKind.Unspecified)]
    [InlineData("2024-01-02 03:04:05.1234567", "2024-01-02T03:04:05.1234567+00:00", "2024-01-02T03:04:05.1234567", DateTimeKind.Unspecified)]
    [InlineData("2024-01-02", "2024-01-02T00:00:00.0000000+00:00", "2024-01-02T00:00:00.0000000", DateTimeKind.Unspecified)]
    [InlineData("2024-07-02 03:04:05+02:00", "2024-07-02T03:04:05.0000000+02:00", "2024-07-02T01:04:05.0000000Z", DateTimeKind.Utc)]
    [InlineData("2024-01-02 03:04:05-05:30", "2024-01-02T03:04:05.0000000-05:30", "2024-01-02T08:34:05.0000000Z", DateTimeKind.Utc)]
    [InlineData("2024-01-02T03:04:05Z", "2024-01-02T03:04:05.0000000+00:00", "2024-01-02T03:04:05.0000000Z", DateTimeKind.Utc)]
    public async Task Text_dates_read_back_with_the_readers_time_zone_handling(
        string text, string expectedStamp, string expectedMoment, DateTimeKind expectedKind)
    {
        var stampExpected = DateTimeOffset.ParseExact(
            expectedStamp, "o", System.Globalization.CultureInfo.InvariantCulture);
        var momentExpected = DateTime.ParseExact(
            expectedMoment, "o", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(
                "CREATE TABLE TextTemporal (Id INTEGER PRIMARY KEY, Value TEXT NULL); " +
                "INSERT INTO TextTemporal (Id, Value) VALUES (1, '" + text + "');").ConfigureAwait(false);
            var repo = new SqliteScalarTypesRepo(fx.Connection);

            DateTimeOffset readerStamp;
            DateTime readerMoment;
            var cmd = fx.Connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = "SELECT Value, Value FROM TextTemporal WHERE Id = 1";
                var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                await using (((System.IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
                {
                    Assert.True(await reader.ReadAsync().ConfigureAwait(false));
                    readerStamp = reader.GetFieldValue<DateTimeOffset>(0);
                    readerMoment = reader.GetFieldValue<DateTime>(1);
                }
            }

            // Guard the expectations: they are what the reader returns.
            Assert.Equal(stampExpected, readerStamp);
            Assert.Equal(stampExpected.Offset, readerStamp.Offset);
            Assert.Equal(momentExpected, readerMoment);
            Assert.Equal(expectedKind, readerMoment.Kind);

            var stamp = await repo.TextStampAsync(1, CancellationToken.None).ConfigureAwait(false);
            Assert.Equal(readerStamp, stamp);
            Assert.Equal(readerStamp.Offset, stamp.Offset);
            var nullableStamp = await repo.NullableTextStampAsync(1, CancellationToken.None).ConfigureAwait(false);
            Assert.Equal(readerStamp, nullableStamp);
            Assert.Equal(readerStamp.Offset, nullableStamp!.Value.Offset);

            var moment = await repo.TextMomentAsync(1, CancellationToken.None).ConfigureAwait(false);
            Assert.Equal(readerMoment, moment);
            Assert.Equal(readerMoment.Kind, moment.Kind);
            var nullableMoment = await repo.NullableTextMomentAsync(1, CancellationToken.None).ConfigureAwait(false);
            Assert.Equal(readerMoment, nullableMoment);
            Assert.Equal(readerMoment.Kind, nullableMoment!.Value.Kind);
        }
    }

    private static ValueTask CreateNumericTableAsync(SqliteFixture fx) => fx.ExecuteDdlAsync(@"
        CREATE TABLE NumericTyped (Id INTEGER PRIMARY KEY, Stamp NULL, Moment NULL, Span NULL);
        INSERT INTO NumericTyped (Id, Stamp, Moment, Span) VALUES
            (1, julianday('2024-01-02 03:04:05.123'), julianday('2024-01-02 03:04:05.123'), 1.5),
            (2, 2460311, 2460311, 2),
            (3, 2451544.500000011574, 2451544.500000011574, 0.25);");

    private static async Task AssertNumericMatchesReaderAsync(SqliteFixture fx, SqliteScalarTypesRepo repo, int id)
    {
        DateTimeOffset stamp;
        DateTime moment;
        TimeSpan span;
        var cmd = fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT Stamp, Moment, Span, typeof(Stamp), typeof(Span) FROM NumericTyped WHERE Id = " +
                id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            await using (((System.IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
            {
                Assert.True(await reader.ReadAsync().ConfigureAwait(false));
                // Guard the fixture: the values must really be stored as numbers.
                Assert.NotEqual("text", reader.GetString(3), StringComparer.Ordinal);
                Assert.NotEqual("text", reader.GetString(4), StringComparer.Ordinal);
                stamp = reader.GetFieldValue<DateTimeOffset>(0);
                moment = reader.GetFieldValue<DateTime>(1);
                span = reader.GetFieldValue<TimeSpan>(2);
            }
        }

        var scalarStamp = await repo.NumericStampAsync(id, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(stamp, scalarStamp);
        Assert.Equal(stamp.Offset, scalarStamp.Offset);
        var scalarMoment = await repo.NumericMomentAsync(id, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(moment, scalarMoment);
        Assert.Equal(moment.Kind, scalarMoment.Kind);
        Assert.Equal(span, await repo.NumericSpanAsync(id, CancellationToken.None).ConfigureAwait(false));
    }

    private static ValueTask CreateTableAsync(SqliteFixture fx) => fx.ExecuteDdlAsync(
        "CREATE TABLE Typed (Id INTEGER PRIMARY KEY, Stamp TEXT NULL, Span TEXT NULL, Ident NULL);");

    private static async Task AssertMatchesReaderAsync(SqliteFixture fx, SqliteScalarTypesRepo repo, int id)
    {
        DateTimeOffset stamp;
        TimeSpan span;
        Guid ident;
        var cmd = fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "SELECT Stamp, Span, Ident FROM Typed WHERE Id = " +
                id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            await using (((System.IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
            {
                Assert.True(await reader.ReadAsync().ConfigureAwait(false));
                stamp = reader.GetFieldValue<DateTimeOffset>(0);
                span = reader.GetFieldValue<TimeSpan>(1);
                ident = reader.GetFieldValue<Guid>(2);
            }
        }

        var scalarStamp = await repo.StampAsync(id, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(stamp, scalarStamp);
        Assert.Equal(stamp.Offset, scalarStamp.Offset);
        Assert.Equal(span, await repo.SpanAsync(id, CancellationToken.None).ConfigureAwait(false));
        Assert.Equal(ident, await repo.IdentAsync(id, CancellationToken.None).ConfigureAwait(false));
    }
}
