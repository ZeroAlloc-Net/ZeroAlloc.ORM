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
