using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] GroupBy against a real PostgreSQL: each grouped result equals LINQ to Objects'.
// The container runs in UTC, so date_trunc('day', ...) of a DateTime column's timestamptz cuts at
// UTC midnight, as C#'s .Date does on the UTC values Npgsql reads. A DateTimeOffset column is cut
// at the UTC day whatever the session's TimeZone. Each case boots its own container, so related
// checks share a case.
[Trait("Provider", "Postgres")]
public sealed class PostgresLinqGroupByTests
{
    private static readonly DateTime Day1 = new(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = Day1.AddDays(1);

    private static async Task<PostgresFixture> CreateAsync()
    {
        PostgresFixture? fx = null;
        try
        {
            fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(LinqSchema.Ddl).ConfigureAwait(false);
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

    private static Task<int> InsertAsync(
        LinqAggregateRepo repo,
        string? model,
        int input,
        int output,
        DateTime executedAt,
        int? sequence = null)
        => repo.InsertAsync(
            Guid.NewGuid(), Guid.NewGuid(), model, input, output, executedAt, sequence, null, null, null, CancellationToken.None);

    [Fact]
    public async Task Daedalus_q1_tokens_per_model_and_day()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var executions = new LinqAggregateRepo(fx.Connection);
        var repo = new LinqGroupByRepo(fx.Connection);

        // Before `since`: filtered out by the WHERE, before grouping.
        await InsertAsync(executions, "sonnet", 1000, 1000, Day1.AddMinutes(-1)).ConfigureAwait(false);
        // Day 1, the last at 23:30 UTC, still day 1.
        await InsertAsync(executions, "sonnet", 10, 1, Day1.AddHours(8)).ConfigureAwait(false);
        await InsertAsync(executions, "sonnet", 20, 2, Day1.AddHours(23.5)).ConfigureAwait(false);
        await InsertAsync(executions, "opus", 5, 50, Day1.AddHours(12)).ConfigureAwait(false);
        await InsertAsync(executions, null, 7, 7, Day1.AddHours(9)).ConfigureAwait(false);
        // Day 2, the first at midnight UTC exactly.
        await InsertAsync(executions, "sonnet", 1, 1, Day2).ConfigureAwait(false);
        await InsertAsync(executions, "opus", 100, 10, Day2.AddHours(10)).ConfigureAwait(false);
        await InsertAsync(executions, "opus", 100, 20, Day2.AddHours(11)).ConfigureAwait(false);

        var rows = await repo.TokensPerModelAndDayAsync(Day1, CancellationToken.None).ConfigureAwait(false);

        // By day, then by input tokens, most first; the null model is a group of its own.
        rows.Should().Equal(
            new ModelDayTokens("sonnet", Day1, 30, 3, 2),
            new ModelDayTokens(null, Day1, 7, 7, 1),
            new ModelDayTokens("opus", Day1, 5, 50, 1),
            new ModelDayTokens("opus", Day2, 200, 30, 2),
            new ModelDayTokens("sonnet", Day2, 1, 1, 1));
        // date_trunc over timestamptz is a timestamptz, which Npgsql reads as a UTC DateTime.
        rows.Should().AllSatisfy(r => r.Day.Kind.Should().Be(DateTimeKind.Utc));
    }

    [Fact]
    public async Task Daedalus_q2_session_tokens_per_owner_and_day()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqGroupByRepo(fx.Connection);
        var ct = CancellationToken.None;
        var day1 = new DateTimeOffset(Day1);

        await repo.InsertSessionAsync(Guid.NewGuid(), "ann", day1.AddHours(1), 100, 10, ct).ConfigureAwait(false);
        await repo.InsertSessionAsync(Guid.NewGuid(), "ann", day1.AddHours(20), 50, 5, ct).ConfigureAwait(false);
        await repo.InsertSessionAsync(Guid.NewGuid(), "ann", day1.AddDays(2).AddSeconds(-1), 4_000_000_000, 1, ct).ConfigureAwait(false);
        await repo.InsertSessionAsync(Guid.NewGuid(), "bob", day1.AddHours(2), 3, 4, ct).ConfigureAwait(false);

        // 23:30 UTC on day 1 is 01:30 on day 2 in Amsterdam: still day 1, the UTC day.
        await repo.InsertSessionAsync(Guid.NewGuid(), "cid", day1.AddHours(23.5), 6, 7, ct).ConfigureAwait(false);

        var expected = new[]
        {
            new OwnerDayTokens("ann", Day1, 150, 15, 2),
            new OwnerDayTokens("ann", Day2, 4_000_000_000, 1, 1),
            new OwnerDayTokens("bob", Day1, 3, 4, 1),
            new OwnerDayTokens("cid", Day1, 6, 7, 1),
        };

        // A DateTimeOffset column's .Date is the UTC date with Kind Unspecified, as C#'s .Date is
        // on the offset-0 value Npgsql reads; bigint sums stay bigint.
        var rows = await repo.SessionTokensPerOwnerAndDayAsync(ct).ConfigureAwait(false);
        rows.Should().Equal(expected);
        rows.Should().AllSatisfy(r => r.Day.Kind.Should().Be(DateTimeKind.Unspecified));

        // The same days under a session whose TimeZone is not UTC.
        await fx.ExecuteDdlAsync("SET TIME ZONE 'Europe/Amsterdam'").ConfigureAwait(false);
        var amsterdam = await repo.SessionTokensPerOwnerAndDayAsync(ct).ConfigureAwait(false);
        amsterdam.Should().Equal(expected);
        amsterdam.Should().AllSatisfy(r => r.Day.Kind.Should().Be(DateTimeKind.Unspecified));
    }

    [Fact]
    public async Task Null_key_member_forms_one_group()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var executions = new LinqAggregateRepo(fx.Connection);
        var repo = new LinqGroupByRepo(fx.Connection);

        await InsertAsync(executions, null, 1, 0, Day1, sequence: 1).ConfigureAwait(false);
        await InsertAsync(executions, null, 1, 0, Day1).ConfigureAwait(false);
        await InsertAsync(executions, "a", 1, 0, Day1).ConfigureAwait(false);
        await InsertAsync(executions, "b", 1, 0, Day1, sequence: 1).ConfigureAwait(false);
        await InsertAsync(executions, "b", 1, 0, Day1, sequence: 2).ConfigureAwait(false);
        await InsertAsync(executions, "b", 1, 0, Day1).ConfigureAwait(false);

        var rows = await repo.CountPerModelAsync(CancellationToken.None).ConfigureAwait(false);

        // Both null models are one group, as in LINQ. A nullable Sum whose values are all null
        // is 0, as LINQ's is. The null key sorts first, as it does in LINQ.
        var expected = new[]
        {
            new ModelCount(null, 2, 1),
            new ModelCount("a", 1, 0),
            new ModelCount("b", 3, 3),
        };
        rows.Should().Equal(expected);

        // g.Count() renders count(*)::int; the record's long member reads the int4 as a long.
        var intCounts = await repo.IntCountPerModelAsync(CancellationToken.None).ConfigureAwait(false);
        intCounts.Should().Equal(expected);
    }

    [Fact]
    public async Task Average_per_group_keeps_the_fraction()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var executions = new LinqAggregateRepo(fx.Connection);
        var repo = new LinqGroupByRepo(fx.Connection);

        await InsertAsync(executions, "a", 1, 0, Day1.AddHours(1), sequence: 1).ConfigureAwait(false);
        await InsertAsync(executions, "a", 2, 0, Day1.AddHours(3)).ConfigureAwait(false);
        await InsertAsync(executions, "b", 3, 0, Day2).ConfigureAwait(false);

        var rows = await repo.AveragesPerModelAsync(CancellationToken.None).ConfigureAwait(false);

        // avg over int is cast to double precision for a double, and kept numeric for a decimal;
        // a nullable average over only nulls is null.
        rows.Should().Equal(
            new ModelAverages("a", 1.5, 1.5m, 1.0, 1, 2, Day1.AddHours(3)),
            new ModelAverages("b", 3.0, 3m, null, 3, 3, Day2));
    }
}
