using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// [LinqCommand] ExecuteDelete against a real PostgreSQL. Each case boots its own container, so
// the rows a case deletes are its own.
[Trait("Provider", "Postgres")]
public sealed class PostgresLinqCommandTests
{
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

    private static async Task<Guid> InsertTaskAsync(LinqCommandRepo repo, string title, WorkStatus status)
    {
        var id = Guid.NewGuid();
        await repo.InsertTaskAsync(id, title, status, CancellationToken.None).ConfigureAwait(false);
        return id;
    }

    // Each delete returns the rows it removed, and leaves the others: by a status, by titles no
    // longer listed through an array parameter, and by an IN subquery over another table.
    [Fact]
    public async Task Delete_returns_rows_affected()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqCommandRepo(fx.Connection);
        var done = await InsertTaskAsync(repo, "a", WorkStatus.Done).ConfigureAwait(false);
        await InsertTaskAsync(repo, "b", WorkStatus.Done).ConfigureAwait(false);
        var open = await InsertTaskAsync(repo, "c", WorkStatus.Open).ConfigureAwait(false);
        await InsertTaskAsync(repo, "d", WorkStatus.Open).ConfigureAwait(false);
        await InsertTaskAsync(repo, "e", WorkStatus.Open).ConfigureAwait(false);
        await repo.InsertExecutionAsync(Guid.NewGuid(), done, CancellationToken.None).ConfigureAwait(false);
        await repo.InsertExecutionAsync(Guid.NewGuid(), done, CancellationToken.None).ConfigureAwait(false);
        await repo.InsertExecutionAsync(Guid.NewGuid(), open, CancellationToken.None).ConfigureAwait(false);

        (await repo.DeleteExecutionsOfDoneAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(2);
        (await repo.CountExecutionsAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(1);

        (await repo.DeleteByStatusAsync(WorkStatus.Done, CancellationToken.None).ConfigureAwait(false)).Should().Be(2);
        (await repo.DeleteByStatusAsync(WorkStatus.Done, CancellationToken.None).ConfigureAwait(false)).Should().Be(0);
        (await repo.CountTasksAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(3);

        (await repo.DeleteOtherTitlesAsync(["c", "x"], CancellationToken.None).ConfigureAwait(false)).Should().Be(2);
        (await repo.CountTasksAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(1);

        // No title listed: every remaining row goes, as C#'s !Contains over an empty array is true.
        (await repo.DeleteOtherTitlesAsync([], CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
        (await repo.CountTasksAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(0);
    }

    // As EF's ExecuteDelete, a chain without Where deletes every row.
    [Fact]
    public async Task Delete_without_where_deletes_every_row()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqCommandRepo(fx.Connection);
        for (var i = 0; i < 4; i++)
            await InsertTaskAsync(repo, "t" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), WorkStatus.Open).ConfigureAwait(false);

        await repo.DeleteAllAsync(CancellationToken.None).ConfigureAwait(false);

        (await repo.CountTasksAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(0);
    }

    // The first delete removes the row; the second finds nothing and throws, naming the method.
    [Fact]
    public async Task Throw_on_no_rows_throws_concurrency_exception()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqCommandRepo(fx.Connection);
        var id = await InsertTaskAsync(repo, "a", WorkStatus.Open).ConfigureAwait(false);

        await repo.DeleteExactlyAsync(id, CancellationToken.None).ConfigureAwait(false);
        var again = () => repo.DeleteExactlyAsync(id, CancellationToken.None);

        await again.Should().ThrowExactlyAsync<ZeroAllocOrmConcurrencyException>()
            .WithMessage("'LinqCommandRepo.DeleteExactlyAsync' affected no rows; the row was changed or removed since it was read")
            .ConfigureAwait(false);
        (await repo.CountTasksAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(0);
    }

    // The delete runs inside the caller's transaction, so a rollback brings the row back.
    [Fact]
    public async Task Delete_inside_caller_transaction_rolls_back()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqCommandRepo(fx.Connection);
        var id = await InsertTaskAsync(repo, "a", WorkStatus.Open).ConfigureAwait(false);
        await InsertTaskAsync(repo, "b", WorkStatus.Open).ConfigureAwait(false);

        var tx = await fx.Connection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
        await using (tx.ConfigureAwait(false))
        {
            (await repo.DeleteByIdAsync(id, tx, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }

        (await repo.CountTasksAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(2);
    }
}
