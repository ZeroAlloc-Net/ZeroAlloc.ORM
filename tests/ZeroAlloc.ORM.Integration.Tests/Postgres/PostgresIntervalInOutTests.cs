using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// #255 — InputOutput TimeSpan against a Postgres INOUT interval. Before the
// fix, the generator declared DbType.Time on every TimeSpan output,
// including InputOutput ones. Npgsql then encoded the *input* value as a
// Postgres `time`, which throws "22008: time out of range" for 24 hours or
// more. Each test sends a value the procedure both reads (to prove the
// value actually reached the server, not just NULL) and mutates, then reads
// back — a real round trip, not just an echo.
//
// The procedure adds an hour to `span` and subtracts 30 minutes from
// `other`; asserting the mutated value confirms Npgsql both sent and
// received an `interval`, not a `time` that would have rejected the send
// outright for these magnitudes.
[Trait("Provider", "Postgres")]
public sealed class PostgresIntervalInOutTests
{
    private const string CreateProcedureSql = @"
        CREATE PROCEDURE inout_interval_proc(INOUT span interval, INOUT other interval)
            LANGUAGE plpgsql
        AS $$
        BEGIN
            span := span + interval '1 hour';
            other := other - interval '30 minutes';
        END;
        $$;";

    [Fact]
    public async Task Inout_interval_over_24_hours_round_trips()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(CreateProcedureSql).ConfigureAwait(false);

        var repo = new PostgresIntervalInOutRepo(fx.Connection);
        var (span, other) = await repo.RoundTripAsync(
            TimeSpan.FromHours(30),
            TimeSpan.FromHours(25) + TimeSpan.FromMinutes(30),
            CancellationToken.None).ConfigureAwait(false);

        span.Should().Be(TimeSpan.FromHours(31), "a time would have wrapped or rejected 30 hours on the way in");
        other.Should().Be(TimeSpan.FromHours(25));
    }

    [Fact]
    public async Task Inout_interval_over_multiple_days_round_trips()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(CreateProcedureSql).ConfigureAwait(false);

        var repo = new PostgresIntervalInOutRepo(fx.Connection);
        var span = TimeSpan.FromDays(400) + TimeSpan.FromHours(5);
        var other = TimeSpan.FromDays(40);
        var (spanResult, otherResult) = await repo.RoundTripAsync(span, other, CancellationToken.None)
            .ConfigureAwait(false);

        spanResult.Should().Be(span + TimeSpan.FromHours(1));
        otherResult.Should().Be(other - TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task Inout_interval_negative_values_round_trip()
    {
        await using var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(CreateProcedureSql).ConfigureAwait(false);

        var repo = new PostgresIntervalInOutRepo(fx.Connection);
        var span = -(TimeSpan.FromHours(30));
        var other = -(TimeSpan.FromDays(400) + TimeSpan.FromHours(5));
        var (spanResult, otherResult) = await repo.RoundTripAsync(span, other, CancellationToken.None)
            .ConfigureAwait(false);

        spanResult.Should().Be(span + TimeSpan.FromHours(1));
        otherResult.Should().Be(other - TimeSpan.FromMinutes(30));
    }
}
