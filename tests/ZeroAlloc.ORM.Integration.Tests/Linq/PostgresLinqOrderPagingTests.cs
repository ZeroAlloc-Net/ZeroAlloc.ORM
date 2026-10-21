using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqQuery] ordering, paging, keyset rows and terminals against a real PostgreSQL. Each case
// boots its own container, so related checks share a case.
[Trait("Provider", "Postgres")]
public sealed class PostgresLinqOrderPagingTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

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

    private static Task<int> InsertAsync(PostgresFixture fx, Guid id, string title, int order, DateTimeOffset createdAt, WorkKind kind = WorkKind.Bug)
        => new LinqRepo(fx.Connection).InsertTaskAsync(
            id, ProjectId.From(Guid.NewGuid()), title, WorkStatus.Open, kind.ToString(), createdAt, null, [], order, CancellationToken.None);

    // 25 rows over three CreatedAt values, so every page boundary falls inside a run of equal
    // timestamps and only the Id tie-breaker keeps a row from being skipped or repeated.
    [Fact]
    public async Task Keyset_paging_walks_all_rows_once()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var seeded = new List<(Guid Id, DateTimeOffset CreatedAt)>();
        for (var i = 0; i < 25; i++)
        {
            var row = (Id: Guid.NewGuid(), CreatedAt: Created.AddMinutes(i % 3));
            seeded.Add(row);
            await InsertAsync(fx, row.Id, "t" + i, i, row.CreatedAt).ConfigureAwait(false);
        }

        // PostgreSQL orders uuid by its bytes, which is the ordinal order of the lowercase text.
        var expected = seeded
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id.ToString("D"), StringComparer.Ordinal)
            .Select(r => r.Id)
            .ToList();

        var repo = new LinqOrderPagingRepo(fx.Connection);
        var seen = new List<Guid>();
        var pageSizes = new List<int>();
        var page = await repo.FirstPageAsync(10, CancellationToken.None).ConfigureAwait(false);
        while (page.Count > 0)
        {
            pageSizes.Add(page.Count);
            foreach (var row in page)
            {
                seen.Add(row.Id);
            }
            var last = page[^1];
            page = await repo.NextPageAsync(last.CreatedAt, last.Id, 10, CancellationToken.None).ConfigureAwait(false);
        }

        seen.Should().Equal(expected);
        pageSizes.Should().Equal(10, 10, 5);

        // OFFSET paging walks the same rows by Order, descending.
        var offsetPage = await repo.OffsetPageAsync(20, 10, CancellationToken.None).ConfigureAwait(false);
        offsetPage.Select(r => r.Order).Should().Equal(4, 3, 2, 1, 0);

        // A negative count reads as LINQ does: Skip(-5) skips nothing, Take(-1) takes nothing.
        var clamped = await repo.OffsetPageAsync(-5, 3, CancellationToken.None).ConfigureAwait(false);
        clamped.Select(r => r.Order).Should().Equal(24, 23, 22);
        (await repo.OffsetPageAsync(0, -1, CancellationToken.None).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Fact]
    public async Task Count_and_any_with_filter()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        for (var i = 0; i < 6; i++)
        {
            await InsertAsync(fx, Guid.NewGuid(), "t" + i, i, Created, i % 2 == 0 ? WorkKind.Bug : WorkKind.Feature).ConfigureAwait(false);
        }
        var repo = new LinqOrderPagingRepo(fx.Connection);

        (await repo.CountAtLeastAsync(2, CancellationToken.None).ConfigureAwait(false)).Should().Be(4);
        (await repo.CountAtLeastAsync(10, CancellationToken.None).ConfigureAwait(false)).Should().Be(0);

        // Kind is stored by name; the enum parameter binds by name inside the counted chain too.
        (await repo.CountKindAsync(WorkKind.Feature, CancellationToken.None).ConfigureAwait(false)).Should().Be(3L);

        (await repo.AnyTitledAsync("t3", CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();
        (await repo.AnyTitledAsync("nope", CancellationToken.None).ConfigureAwait(false)).Should().BeFalse();

        // Paging applies before the count: 4 rows match, the page after the first 3 holds 1.
        (await repo.CountPageAsync(2, 0, 3, CancellationToken.None).ConfigureAwait(false)).Should().Be(3);
        (await repo.CountPageAsync(2, 3, 3, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
        (await repo.CountPageAsync(2, 4, 3, CancellationToken.None).ConfigureAwait(false)).Should().Be(0);
        (await repo.AnyPageAsync(2, 3, CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();
        (await repo.AnyPageAsync(2, 4, CancellationToken.None).ConfigureAwait(false)).Should().BeFalse();

        // The label is only in the projection Count drops, so it is not bound at all.
        (await repo.CountLabeledAsync("unused", 2, CancellationToken.None).ConfigureAwait(false)).Should().Be(4);
    }

    [Fact]
    public async Task Query_runs_on_caller_transaction()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqOrderPagingRepo(fx.Connection);
        var id = Guid.NewGuid();

        // Npgsql runs a command on the connection's open transaction whether or not the command
        // names it, so this case cannot tell a passed-through transaction from a dropped one.
        // The generator snapshot Count_long_count_any pins `__cmd.Transaction = @tx;` instead.
        var tx = await fx.Connection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
        await using (tx.ConfigureAwait(false))
        {
            await repo.InsertInTransactionAsync(id, Guid.NewGuid(), "inside", Created, 7, tx, CancellationToken.None).ConfigureAwait(false);

            (await repo.CountAtLeastInTransactionAsync(7, tx, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
            var rows = await repo.TitledInTransactionAsync("inside", tx, CancellationToken.None).ConfigureAwait(false);
            rows.Should().ContainSingle().Which.Id.Should().Be(id);

            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }

        (await repo.CountAtLeastAsync(7, CancellationToken.None).ConfigureAwait(false)).Should().Be(0);
    }
}
