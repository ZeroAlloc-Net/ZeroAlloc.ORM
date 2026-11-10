using System.Data.Async.Adapters;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using ZeroAlloc.ORM.Integration.Tests.TestDoubles;

namespace ZeroAlloc.ORM.Integration.Tests;

// #464 — {table} resolves, per repository instance, to the table name the instance was built
// with. In-memory Sqlite round trips.
public class TablePlaceholderTests
{
    [Fact]
    public async Task Insert_And_Query_UseConfiguredTable()
    {
        var fx = await SeededAsync("Widgets").ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var repo = new TablePlaceholderRepo(fx.Connection, "Widgets");

            (await repo.InsertAsync(1, "bolt", CancellationToken.None).ConfigureAwait(false)).Should().Be(1);

            (await repo.GetNameAsync(1, CancellationToken.None).ConfigureAwait(false)).Should().Be("bolt");
            (await repo.CountAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
        }
    }

    [Fact]
    public async Task TwoInstancesDifferentTables_AreIsolated()
    {
        var fx = await SeededAsync("Widgets", "Gadgets").ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var widgets = new TablePlaceholderRepo(fx.Connection, "Widgets");
            var gadgets = new TablePlaceholderRepo(fx.Connection, "Gadgets");

            await widgets.InsertAsync(1, "bolt", CancellationToken.None).ConfigureAwait(false);
            await gadgets.InsertAsync(1, "gear", CancellationToken.None).ConfigureAwait(false);
            await gadgets.InsertAsync(2, "lever", CancellationToken.None).ConfigureAwait(false);

            (await widgets.GetNameAsync(1, CancellationToken.None).ConfigureAwait(false)).Should().Be("bolt");
            (await gadgets.GetNameAsync(1, CancellationToken.None).ConfigureAwait(false)).Should().Be("gear");
            (await widgets.CountAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
            (await gadgets.CountAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(2);
        }
    }

    [Fact]
    public async Task OverloadsWithPlaceholders_BothWork()
    {
        var fx = await SeededAsync("Widgets").ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var repo = new TablePlaceholderRepo(fx.Connection, "Widgets");
            for (var id = 1; id <= 3; id++)
                await repo.InsertAsync(id, "w" + id, CancellationToken.None).ConfigureAwait(false);

            (await repo.CountAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(3);
            (await repo.CountAsync(2, CancellationToken.None).ConfigureAwait(false)).Should().Be(2);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NullTable_ThrowsArgumentException(string? table)
    {
        var fx = await SeededAsync("Widgets").ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var repo = new TablePlaceholderRepo(fx.Connection, table!);

            var act = async () => await repo.CountAsync(CancellationToken.None).ConfigureAwait(false);

            await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task ConcurrentFirstUse_AllCallsSucceed()
    {
        const int Callers = 32;
        // A shared-cache in-memory database, so every thread can have its own connection to it.
        var connectionString = "Data Source=table-placeholder-" + Guid.NewGuid().ToString("N") + ";Mode=Memory;Cache=Shared";
        var keeper = new SqliteConnection(connectionString);
        await using (keeper.ConfigureAwait(false))
        {
            await keeper.OpenAsync().ConfigureAwait(false);
            using (var ddl = keeper.CreateCommand())
            {
                ddl.CommandText = "CREATE TABLE Widgets (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL)";
                await ddl.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            using var perThread = new ThreadLocalSqliteConnection(connectionString);
            var repo = new TablePlaceholderRepo(perThread.AsAsync(), "Widgets");

            // A smoke test, not proof of memory ordering: every caller waits at the barrier, so the
            // first calls into the fresh instance are likely to overlap, and each must see the SQL
            // for its table. Whether a reader can see a half-published entry depends on the hardware
            // memory model; the emitted code rules that out by publishing one immutable holder.
            using var barrier = new Barrier(Callers);
            var results = new string?[Callers];
            var errors = new Exception?[Callers];
            var threads = new Thread[Callers];
            for (var t = 0; t < Callers; t++)
            {
                var id = t + 1;
                threads[t] = new Thread(() =>
                {
                    try
                    {
                        barrier.SignalAndWait();
                        repo.InsertAsync(id, "w" + id, CancellationToken.None).GetAwaiter().GetResult();
                        results[id - 1] = repo.GetNameAsync(id, CancellationToken.None).GetAwaiter().GetResult();
                    }
                    catch (Exception e)
                    {
                        errors[id - 1] = e;
                    }
                });
                threads[t].Start();
            }
            foreach (var thread in threads)
                thread.Join();

            errors.Should().AllSatisfy(e => e.Should().BeNull());
            for (var id = 1; id <= Callers; id++)
                results[id - 1].Should().Be("w" + id);
        }
    }

    private static async Task<SqliteFixture> SeededAsync(params string[] tables)
    {
        var fx = new SqliteFixture();
        await fx.InitializeAsync().ConfigureAwait(false);
        foreach (var table in tables)
            await fx.ExecuteDdlAsync($"CREATE TABLE {table} (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL)").ConfigureAwait(false);
        return fx;
    }
}
