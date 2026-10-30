using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// A DateTime parameter compared with or set into a column whose map declares its storage binds as
// that column's type, issue 403: NpgsqlDbType.Timestamp or TimestampTz, as EF Core's Npgsql
// provider binds it. Npgsql then rejects a value whose Kind does not fit the column before the
// command is sent: a timestamptz takes only Kind Utc, a timestamp anything but Utc. Left to infer
// the type from the Kind, such a value reached PostgreSQL as the other type and was shifted
// through the session's TimeZone without a word. The session here runs at UTC+2, so any shift
// would show. A column whose storage is Default keeps the inferred type, and with it the shift.
[Trait("Provider", "Postgres")]
public sealed class PostgresLinqDateTimeStorageTests
{
    private static readonly Guid Id = new("6f1c2a52-7d1e-4a43-9a43-7a2f0d2d1c01");

    // 2026-10-21 09:30, as a wall clock and as a UTC moment.
    private static readonly DateTime Unspecified = new(2026, 10, 21, 9, 30, 0, DateTimeKind.Unspecified);
    private static readonly DateTime Local = new(2026, 10, 21, 9, 30, 0, DateTimeKind.Local);
    private static readonly DateTime Utc = new(2026, 10, 21, 9, 30, 0, DateTimeKind.Utc);

    private const string Ddl = """
        SET TIME ZONE 'Etc/GMT-2';
        CREATE TABLE "Readings" (
            "Id" uuid PRIMARY KEY,
            "TakenAt" timestamp NOT NULL,
            "RecordedAt" timestamptz NOT NULL,
            "SeenAt" timestamptz NOT NULL,
            "ClosedAt" timestamptz NULL,
            "CheckedAt" timestamp NULL
        );
        INSERT INTO "Readings" ("Id", "TakenAt", "RecordedAt", "SeenAt")
        VALUES ('6f1c2a52-7d1e-4a43-9a43-7a2f0d2d1c01', '2026-10-21 09:30:00', '2026-10-21 09:30:00+00', '2026-10-21 09:30:00+00');
        """;

    private static async Task<PostgresFixture> CreateAsync()
    {
        PostgresFixture? fx = null;
        try
        {
            fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(Ddl).ConfigureAwait(false);
            return fx;
        }
        catch
        {
            if (fx is not null)
            {
                await fx.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    // A timestamp holds a wall clock: Unspecified and Local values are sent as theirs, unshifted.
    [Fact]
    public async Task Timestamp_column_matches_Unspecified_and_Local_values_exactly()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqReadingRepo(fx.Connection);

        (await repo.TakenAtAsync(Unspecified, CancellationToken.None).ConfigureAwait(false)).Should().Equal(new ReadingId(Id));
        (await repo.TakenAtAsync(Local, CancellationToken.None).ConfigureAwait(false)).Should().Equal(new ReadingId(Id));
        (await repo.TakenAtAsync(Unspecified.AddHours(2), CancellationToken.None).ConfigureAwait(false)).Should().BeEmpty();
    }

    // Before 403 a Utc value went as a timestamptz and met the column at the session's UTC+2:
    // 09:30 UTC matched no row stored at 09:30.
    [Fact]
    public async Task Timestamp_column_rejects_a_Utc_value()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqReadingRepo(fx.Connection);

        await AssertRejectedAsync(() => repo.TakenAtAsync(Utc, CancellationToken.None), "UTC").ConfigureAwait(false);
    }

    [Fact]
    public async Task Timestamptz_column_matches_a_Utc_value_whatever_the_session_TimeZone()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqReadingRepo(fx.Connection);

        (await repo.RecordedAtAsync(Utc, CancellationToken.None).ConfigureAwait(false)).Should().Equal(new ReadingId(Id));
        (await repo.RecordedAtAsync(Utc.AddHours(-2), CancellationToken.None).ConfigureAwait(false)).Should().BeEmpty();
    }

    // Before 403 an Unspecified or Local value went as a timestamp, read at the session's UTC+2.
    [Fact]
    public async Task Timestamptz_column_rejects_Unspecified_and_Local_values()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqReadingRepo(fx.Connection);

        await AssertRejectedAsync(() => repo.RecordedAtAsync(Unspecified, CancellationToken.None), "Unspecified").ConfigureAwait(false);
        await AssertRejectedAsync(() => repo.RecordedAtAsync(Local, CancellationToken.None), "Local").ConfigureAwait(false);
    }

    // Default storage keeps the type Npgsql infers from the Kind: a Utc value is a timestamptz, an
    // Unspecified one a timestamp, which PostgreSQL reads at the session's UTC+2.
    [Fact]
    public async Task Default_storage_column_keeps_the_type_inferred_from_the_Kind()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqReadingRepo(fx.Connection);

        (await repo.SeenAtAsync(Utc, CancellationToken.None).ConfigureAwait(false)).Should().Equal(new ReadingId(Id));
        (await repo.SeenAtAsync(Unspecified.AddHours(2), CancellationToken.None).ConfigureAwait(false)).Should().Equal(new ReadingId(Id));
        (await repo.SeenAtAsync(Unspecified, CancellationToken.None).ConfigureAwait(false)).Should().BeEmpty();
    }

    // The null test casts the parameter to the timestamp it binds as.
    [Fact]
    public async Task Nullable_parameter_on_a_timestamp_column_tests_null_and_compares_exactly()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqReadingRepo(fx.Connection);

        (await repo.TakenUpToAsync(null, CancellationToken.None).ConfigureAwait(false)).Should().Equal(new ReadingId(Id));
        (await repo.TakenUpToAsync(Unspecified, CancellationToken.None).ConfigureAwait(false)).Should().Equal(new ReadingId(Id));
        (await repo.TakenUpToAsync(Unspecified.AddMinutes(-1), CancellationToken.None).ConfigureAwait(false)).Should().BeEmpty();
        await AssertRejectedAsync(() => repo.TakenUpToAsync(Utc, CancellationToken.None), "UTC").ConfigureAwait(false);
    }

    [Fact]
    public async Task Setter_into_a_timestamptz_column_stores_a_Utc_value_and_rejects_others()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqReadingRepo(fx.Connection);

        await AssertRejectedAsync(() => repo.CloseAsync(Id, Unspecified, CancellationToken.None), "Unspecified").ConfigureAwait(false);
        await AssertRejectedAsync(() => repo.CloseAsync(Id, Local, CancellationToken.None), "Local").ConfigureAwait(false);
        (await repo.CloseAsync(Id, Utc, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);

        var line = await repo.GetAsync(Id, CancellationToken.None).ConfigureAwait(false);
        line.Should().Be(new ReadingLine(Id, Utc, null));
        line!.ClosedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Setter_into_a_timestamp_column_stores_the_wall_clock_and_rejects_Utc()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqReadingRepo(fx.Connection);

        await AssertRejectedAsync(() => repo.CheckAsync(Id, Utc, CancellationToken.None), "UTC").ConfigureAwait(false);
        (await repo.CheckAsync(Id, Local.AddHours(1), CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
        (await repo.GetAsync(Id, CancellationToken.None).ConfigureAwait(false))
            .Should().Be(new ReadingLine(Id, null, Unspecified.AddHours(1)));
        (await repo.CheckAsync(Id, Unspecified, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);

        var line = await repo.GetAsync(Id, CancellationToken.None).ConfigureAwait(false);
        line.Should().Be(new ReadingLine(Id, null, Unspecified));
        line!.CheckedAt!.Value.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    // Npgsql rejects the value client-side, naming its Kind, before the command is sent.
    private static async Task AssertRejectedAsync(Func<Task> act, string kind)
        => await act.Should().ThrowExactlyAsync<ArgumentException>()
            .WithMessage("Cannot write DateTime with Kind=" + kind + " to PostgreSQL type*")
            .ConfigureAwait(false);
}
