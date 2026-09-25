using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// #245, #247 — temporal stored-procedure outputs on Npgsql. See
// PostgresTemporalOutputRepo for the CLR types Npgsql hands back.
[Trait("Provider", "Postgres")]
public sealed class PostgresTemporalOutputTests
{
    private static readonly DateTimeOffset Stamp =
        new DateTimeOffset(2024, 1, 2, 1, 4, 5, TimeSpan.Zero).AddTicks(1_234_560);

    private static readonly TimeSpan Clock = new TimeSpan(0, 13, 14, 15).Add(TimeSpan.FromTicks(1_234_560));

    [Fact]
    public async Task Timestamptz_output_reads_back_as_DateTimeOffset()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE PROCEDURE stamp_proc(OUT stamp timestamptz, OUT missing timestamptz)
                LANGUAGE plpgsql
            AS $$
            BEGIN
                stamp := '2024-01-02 03:04:05.123456+02';
                missing := NULL;
            END;
            $$;").ConfigureAwait(false);

        var repo = new PostgresTemporalOutputRepo(fx.Connection);
        var (stamp, missing) = await repo.StampAsync(default, null, CancellationToken.None).ConfigureAwait(false);

        stamp.Should().Be(Stamp, "the same instant as the value the procedure set");
        stamp.Offset.Should().Be(TimeSpan.Zero, "Npgsql reads timestamptz as UTC, as GetFieldValue<DateTimeOffset> does");
        missing.Should().BeNull();
    }

    [Fact]
    public async Task Time_output_reads_back_as_TimeSpan()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE PROCEDURE clock_proc(OUT clock time, OUT late time)
                LANGUAGE plpgsql
            AS $$
            BEGIN
                clock := '13:14:15.123456';
                late := '23:59:59.999999';
            END;
            $$;").ConfigureAwait(false);

        var repo = new PostgresTemporalOutputRepo(fx.Connection);
        var (clock, late) = await repo.ClockAsync(default, null, CancellationToken.None).ConfigureAwait(false);

        clock.Should().Be(Clock);
        late.Should().Be(TimeSpan.FromDays(1) - TimeSpan.FromTicks(10));
    }

    [Fact]
    public async Task Interval_outputs_below_and_above_a_day_read_back_as_TimeSpan()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE PROCEDURE interval_proc(OUT brief interval, OUT lengthy interval)
                LANGUAGE plpgsql
            AS $$
            BEGIN
                brief := '1 hour 30 minutes 0.5 seconds';
                lengthy := '3 days 4 hours 5 minutes 6.789 seconds';
            END;
            $$;").ConfigureAwait(false);

        var repo = new PostgresTemporalOutputRepo(fx.Connection);
        var (brief, lengthy) = await repo.IntervalAsync(default, default, CancellationToken.None).ConfigureAwait(false);

        brief.Should().Be(new TimeSpan(0, 1, 30, 0, 500));
        lengthy.Should().Be(new TimeSpan(3, 4, 5, 6, 789), "an interval is not wrapped at 24 hours as a time is");
    }

    [Fact]
    public async Task Date_timestamp_and_timestamptz_outputs_read_back_as_DateTime()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(@"
            CREATE PROCEDURE dates_proc(OUT day date, OUT plain timestamp, OUT utc timestamptz)
                LANGUAGE plpgsql
            AS $$
            BEGIN
                day := '2024-01-02';
                plain := '2024-01-02 03:04:05.123456';
                utc := '2024-01-02 03:04:05.123456+02';
            END;
            $$;").ConfigureAwait(false);

        var repo = new PostgresTemporalOutputRepo(fx.Connection);
        var (day, plain, utc) = await repo.DatesAsync(default, default, default, CancellationToken.None).ConfigureAwait(false);

        day.Should().Be(new DateTime(2024, 1, 2));
        plain.Should().Be(new DateTime(2024, 1, 2, 3, 4, 5).AddTicks(1_234_560));
        utc.Should().Be(new DateTime(2024, 1, 2, 1, 4, 5, DateTimeKind.Utc).AddTicks(1_234_560));
        utc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Scalar_timestamptz_and_time_read_back_as_DateTimeOffset_and_TimeSpan()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);

        var repo = new PostgresTemporalOutputRepo(fx.Connection);

        (await repo.ScalarStampAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(Stamp);
        (await repo.ScalarClockAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(Clock);
    }
}
