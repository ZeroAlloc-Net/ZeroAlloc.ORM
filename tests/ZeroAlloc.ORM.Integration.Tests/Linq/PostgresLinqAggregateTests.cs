using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] scalar aggregates against a real PostgreSQL: each result equals LINQ to Objects',
// and each nullable one reads with the strict reader of its CLR type. Each case boots its own
// container, so related checks share a case.
[Trait("Provider", "Postgres")]
public sealed class PostgresLinqAggregateTests
{
    private static readonly DateTime Executed = new(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc);

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
        Guid taskId,
        int input,
        int output,
        int? sequence = null,
        string? model = null,
        DateTime? executedAt = null)
        => repo.InsertAsync(Guid.NewGuid(), taskId, model, input, output, executedAt ?? Executed, sequence, CancellationToken.None);

    [Fact]
    public async Task Sum_of_no_rows_is_zero()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqAggregateRepo(fx.Connection);
        var task = Guid.NewGuid();
        var none = Guid.NewGuid();
        await InsertAsync(repo, task, 3, 10).ConfigureAwait(false);
        await InsertAsync(repo, task, 1, 0).ConfigureAwait(false);
        await InsertAsync(repo, task, 2, 5).ConfigureAwait(false);
        var ct = CancellationToken.None;

        // No rows match: every Sum is 0, as LINQ's is.
        (await repo.SumInputAsync(none, ct).ConfigureAwait(false)).Should().Be(0);
        (await repo.SumInputWideAsync(none, ct).ConfigureAwait(false)).Should().Be(0L);
        (await repo.SumOutputDoubleAsync(none, ct).ConfigureAwait(false)).Should().Be(0d);
        (await repo.SumSequenceAsync(none, ct).ConfigureAwait(false)).Should().Be(0);

        // Rows match, but every Sequence is null: a nullable Sum is still 0, not null.
        (await repo.SumSequenceAsync(task, ct).ConfigureAwait(false)).Should().Be(0);
        (await repo.SumSequenceWideAsync(task, ct).ConfigureAwait(false)).Should().Be(0L);
        (await repo.SumSequenceDecimalAsync(task, ct).ConfigureAwait(false)).Should().Be(0m);

        // With values, each in its own CLR type: sum(int) is bigint and is cast back to int.
        (await repo.SumInputAsync(task, ct).ConfigureAwait(false)).Should().Be(6);
        (await repo.SumInputWideAsync(task, ct).ConfigureAwait(false)).Should().Be(6L);
        (await repo.SumOutputDoubleAsync(task, ct).ConfigureAwait(false)).Should().Be(15d);

        // A paged sum reads the page the ORDER BY picks: the two smallest inputs, 1 and 2.
        (await repo.SumSmallestInputAsync(2, ct).ConfigureAwait(false)).Should().Be(3);
        (await repo.SumSmallestInputAsync(0, ct).ConfigureAwait(false)).Should().Be(0);
    }

    [Fact]
    public async Task Average_keeps_the_fraction()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqAggregateRepo(fx.Connection);
        var task = Guid.NewGuid();
        await InsertAsync(repo, task, 1, 0, sequence: 1, model: "sonnet", executedAt: Executed).ConfigureAwait(false);
        await InsertAsync(repo, task, 2, 0, sequence: 2, model: "haiku", executedAt: Executed.AddHours(1)).ConfigureAwait(false);
        var ct = CancellationToken.None;

        (await repo.AverageInputAsync(task, ct).ConfigureAwait(false)).Should().Be(1.5d);
        (await repo.AverageInputDecimalAsync(task, ct).ConfigureAwait(false)).Should().Be(1.5m);
        (await repo.AverageSequenceDecimalAsync(task, ct).ConfigureAwait(false)).Should().Be(1.5m);
        (await repo.SumSequenceAsync(task, ct).ConfigureAwait(false)).Should().Be(3);
        (await repo.SumSequenceWideAsync(task, ct).ConfigureAwait(false)).Should().Be(3L);
        (await repo.SumSequenceDecimalAsync(task, ct).ConfigureAwait(false)).Should().Be(3m);

        // Min and Max keep the column's type, or the type a widening cast names.
        (await repo.MaxInputAsync(task, ct).ConfigureAwait(false)).Should().Be(2);
        (await repo.MaxInputWideAsync(task, ct).ConfigureAwait(false)).Should().Be(2L);
        (await repo.MinModelAsync(task, ct).ConfigureAwait(false)).Should().Be("haiku");
        var last = await repo.LastExecutedAsync(task, ct).ConfigureAwait(false);
        last.Should().Be(Executed.AddHours(1));
        last!.Value.Kind.Should().Be(DateTimeKind.Utc);

        // Nullable forms of an empty set are null.
        var none = Guid.NewGuid();
        (await repo.AverageSequenceDecimalAsync(none, ct).ConfigureAwait(false)).Should().BeNull();
        (await repo.MaxInputWideAsync(none, ct).ConfigureAwait(false)).Should().BeNull();
        (await repo.MinModelAsync(none, ct).ConfigureAwait(false)).Should().BeNull();
        (await repo.LastExecutedAsync(none, ct).ConfigureAwait(false)).Should().BeNull();
    }

    [Fact]
    public async Task Daedalus_q5_average_and_q6_max()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqAggregateRepo(fx.Connection);
        var task = Guid.NewGuid();
        var other = Guid.NewGuid();
        await InsertAsync(repo, task, 1, 10, sequence: 1).ConfigureAwait(false);
        await InsertAsync(repo, task, 1, 0, sequence: 2).ConfigureAwait(false);
        await InsertAsync(repo, task, 1, 15, sequence: 4).ConfigureAwait(false);
        await InsertAsync(repo, task, 1, 20, sequence: null).ConfigureAwait(false);
        await InsertAsync(repo, other, 1, 100, sequence: 9).ConfigureAwait(false);
        var ct = CancellationToken.None;

        // q5: the mean of 10, 15 and 20; the execution without output does not count.
        (await repo.AverageOutputAsync(task, ct).ConfigureAwait(false)).Should().Be(15d);
        // q6: the highest sequence of this task; a null sequence is skipped, as in LINQ.
        (await repo.MaxSequenceAsync(task, ct).ConfigureAwait(false)).Should().Be(4);

        // No rows: both are null.
        var none = Guid.NewGuid();
        (await repo.AverageOutputAsync(none, ct).ConfigureAwait(false)).Should().BeNull();
        (await repo.MaxSequenceAsync(none, ct).ConfigureAwait(false)).Should().BeNull();

        // Rows, but none with output: still no values to average.
        var idle = Guid.NewGuid();
        await InsertAsync(repo, idle, 1, 0).ConfigureAwait(false);
        (await repo.AverageOutputAsync(idle, ct).ConfigureAwait(false)).Should().BeNull();
        (await repo.MaxSequenceAsync(idle, ct).ConfigureAwait(false)).Should().BeNull();
    }

    // LINQ's non-nullable Average, Min and Max throw on no values. The SQL gives NULL, which the
    // non-null scalar shape cannot convert.
    [Fact]
    public async Task Non_nullable_average_of_no_rows_throws()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqAggregateRepo(fx.Connection);
        var none = Guid.NewGuid();

        var average = () => repo.AverageInputAsync(none, CancellationToken.None);
        await average.Should().ThrowAsync<InvalidCastException>().ConfigureAwait(false);

        var averageDecimal = () => repo.AverageInputDecimalAsync(none, CancellationToken.None);
        await averageDecimal.Should().ThrowAsync<InvalidCastException>().ConfigureAwait(false);

        var max = () => repo.MaxInputAsync(none, CancellationToken.None);
        await max.Should().ThrowAsync<InvalidCastException>().ConfigureAwait(false);
    }
}
