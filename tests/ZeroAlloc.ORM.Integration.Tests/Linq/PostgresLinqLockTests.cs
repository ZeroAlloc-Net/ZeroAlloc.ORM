using System.Data.Async.Adapters;
using AwesomeAssertions;
using Npgsql;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// Row locks against a real PostgreSQL, with a second connection as the other transaction. Each
// case boots its own container, so the rows a case writes are its own.
[Trait("Provider", "Postgres")]
public sealed class PostgresLinqLockTests
{
    // How long a step that should finish promptly may take before the case fails.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

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

    // A second connection to the same database, the other transaction.
    private static async Task<NpgsqlConnection> OpenOtherAsync(PostgresFixture fx)
    {
        var raw = new NpgsqlConnection(fx.ConnectionString);
        await raw.OpenAsync().ConfigureAwait(false);
        return raw;
    }

    // Waits until the backend `pid` is blocked by another's lock, polling pg_blocking_pids from a
    // third connection, so the case proves the wait rather than guessing it from a sleep.
    private static async Task WaitUntilBlockedAsync(PostgresFixture fx, int pid)
    {
        var watcher = await OpenOtherAsync(fx).ConfigureAwait(false);
        await using (watcher.ConfigureAwait(false))
        {
            var cmd = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@pid))", watcher);
            await using (cmd.ConfigureAwait(false))
            {
                cmd.Parameters.Add(new NpgsqlParameter<int>("pid", pid));
                var deadline = DateTime.UtcNow + Patience;
                while ((int)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))! == 0)
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("The second writer was never blocked.");
                    await Task.Delay(20).ConfigureAwait(false);
                }
            }
        }
    }

    // FOR UPDATE holds the row until its transaction ends: another writer's UPDATE waits on it,
    // and goes through once the lock's transaction commits.
    [Fact]
    public async Task For_update_blocks_a_second_writer_until_commit()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqLockRepo(fx.Connection);
        var id = Guid.NewGuid();
        await repo.InsertTaskAsync(id, "Draft", 0, CancellationToken.None).ConfigureAwait(false);

        var raw = await OpenOtherAsync(fx).ConfigureAwait(false);
        await using (raw.ConfigureAwait(false))
        {
            var other = new LinqLockRepo(raw.AsAsync());
            var tx = await fx.Connection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                var locked = await repo.LockAsync(id, tx, CancellationToken.None).ConfigureAwait(false);
                locked!.Title.Should().Be("Draft");

                var rename = other.RenameAsync(id, "Theirs", CancellationToken.None);
                await WaitUntilBlockedAsync(fx, raw.ProcessID).ConfigureAwait(false);
                rename.IsCompleted.Should().BeFalse();

                await tx.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                (await rename.WaitAsync(Patience).ConfigureAwait(false)).Should().Be(1);
            }
        }

        (await repo.GetAsync(id, CancellationToken.None).ConfigureAwait(false))!.Title.Should().Be("Theirs");
    }

    // SKIP LOCKED passes over the rows another transaction holds: two open transactions claiming
    // from one queue never get the same row, and between them claim every row.
    [Fact]
    public async Task Skip_locked_claims_are_disjoint_across_transactions()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqLockRepo(fx.Connection);
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        for (var i = 0; i < ids.Count; i++)
        {
            await repo.InsertTaskAsync(ids[i], "Job " + i, i, CancellationToken.None).ConfigureAwait(false);
        }

        var raw = await OpenOtherAsync(fx).ConfigureAwait(false);
        await using (raw.ConfigureAwait(false))
        {
            var otherConnection = raw.AsAsync();
            var other = new LinqLockRepo(otherConnection);
            var mine = await fx.Connection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            await using (mine.ConfigureAwait(false))
            {
                var theirs = await otherConnection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
                await using (theirs.ConfigureAwait(false))
                {
                    var first = await repo.ClaimAsync(WorkStatus.Open, 3, mine, CancellationToken.None).ConfigureAwait(false);
                    var second = await other.ClaimAsync(WorkStatus.Open, 3, theirs, CancellationToken.None)
                        .WaitAsync(Patience).ConfigureAwait(false);

                    first.Should().HaveCount(3);
                    second.Should().HaveCount(3);
                    first.Select(t => t.Id).Should().NotIntersectWith(second.Select(t => t.Id));
                    first.Select(t => t.Id).Concat(second.Select(t => t.Id)).Should().BeEquivalentTo(ids);

                    // The first claim takes the queue's head, in order.
                    first.Select(t => t.Order).Should().Equal(0, 1, 2);

                    await theirs.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                await mine.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    // NOWAIT fails at once on a row another transaction holds, with lock_not_available.
    [Fact]
    public async Task No_wait_throws_when_row_is_locked()
    {
        await using var fx = await CreateAsync().ConfigureAwait(false);
        var repo = new LinqLockRepo(fx.Connection);
        var id = Guid.NewGuid();
        await repo.InsertTaskAsync(id, "Draft", 0, CancellationToken.None).ConfigureAwait(false);

        var raw = await OpenOtherAsync(fx).ConfigureAwait(false);
        await using (raw.ConfigureAwait(false))
        {
            var otherConnection = raw.AsAsync();
            var other = new LinqLockRepo(otherConnection);
            var mine = await fx.Connection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            await using (mine.ConfigureAwait(false))
            {
                (await repo.LockAsync(id, mine, CancellationToken.None).ConfigureAwait(false)).Should().NotBeNull();

                var theirs = await otherConnection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
                await using (theirs.ConfigureAwait(false))
                {
                    var noWait = () => other.LockNoWaitAsync(id, theirs, CancellationToken.None).WaitAsync(Patience);
                    (await noWait.Should().ThrowAsync<PostgresException>().ConfigureAwait(false))
                        .Which.SqlState.Should().Be("55P03");
                    await theirs.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }

                await mine.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }

            // Once the lock's transaction ended, the same NOWAIT lock succeeds.
            var again = await otherConnection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            await using (again.ConfigureAwait(false))
            {
                (await other.LockNoWaitAsync(id, again, CancellationToken.None).ConfigureAwait(false))!.Title.Should().Be("Draft");
                await again.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
