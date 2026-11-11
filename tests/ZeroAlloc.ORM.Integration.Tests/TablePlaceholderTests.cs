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
    public async Task MultiStatementQuery_BothStatementsUseTable()
    {
        var fx = await SeededAsync("Widgets", "Gadgets").ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var widgets = new TablePlaceholderRepo(fx.Connection, "Widgets");
            var gadgets = new TablePlaceholderRepo(fx.Connection, "Gadgets");
            for (var id = 1; id <= 4; id++)
                await widgets.InsertAsync(id, "w" + id, CancellationToken.None).ConfigureAwait(false);
            await gadgets.InsertAsync(9, "g9", CancellationToken.None).ConfigureAwait(false);

            var (low, high) = await widgets.SplitAsync(3, CancellationToken.None).ConfigureAwait(false);
            low.Select(r => r.Name).Should().Equal("w1", "w2");
            high.Select(r => r.Name).Should().Equal("w3", "w4");

            var (gLow, gHigh) = await gadgets.SplitAsync(3, CancellationToken.None).ConfigureAwait(false);
            gLow.Should().BeEmpty();
            gHigh.Select(r => r.Name).Should().Equal("g9");
        }
    }

    [Fact]
    public async Task MultiStatementQuery_JoinedFallback_BothStatementsUseTable()
    {
        var fx = await SeededAsync("Widgets").ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var repo = new TablePlaceholderRepo(fx.Connection, "Widgets");
            for (var id = 1; id <= 4; id++)
                await repo.InsertAsync(id, "w" + id, CancellationToken.None).ConfigureAwait(false);

            var (low, high) = await repo.SplitJoinedAsync(3, CancellationToken.None).ConfigureAwait(false);

            low.Select(r => r.Name).Should().Equal("w1", "w2");
            high.Select(r => r.Name).Should().Equal("w3", "w4");
        }
    }

    [Fact]
    public async Task MultiStatementCommand_BothStatementsUseTable()
    {
        var fx = await SeededAsync("Widgets").ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var repo = new TablePlaceholderRepo(fx.Connection, "Widgets");

            await repo.InsertTwoAsync(5, CancellationToken.None).ConfigureAwait(false);

            (await repo.CountAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(2);
        }
    }

    // Sqlite has no batch, so a recording connection with CanCreateBatch = true takes the generated
    // IAsyncDbBatch branch: each batch command must carry its own statement, resolved and trimmed,
    // in order. Batch commands exist for [Query] tuples only; a [Command] with two statements runs
    // as one joined command and has no batch path.
    [Theory]
    [InlineData("Auto")]
    [InlineData("Always")]
    public async Task MultiStatementQuery_Batch_EachCommandCarriesItsResolvedStatement(string mode)
    {
        var rows = new TableResult(["Id", "Name"], [[1, "a"]]);
        var raw = new RecordingBatchDbConnection(rows, rows);
        await using (raw.ConfigureAwait(false))
        {
            var repo = new TablePlaceholderRepo(raw.AsAsync(), "Gizmos");

            if (string.Equals(mode, "Auto", StringComparison.Ordinal))
                await repo.SplitAsync(3, CancellationToken.None).ConfigureAwait(false);
            else
                await repo.SplitBatchAsync(3, CancellationToken.None).ConfigureAwait(false);

            raw.BatchCommandTexts.Should().Equal(
                "SELECT Id, Name FROM Gizmos WHERE Id < @max ORDER BY Id",
                "SELECT Id, Name FROM Gizmos WHERE Id >= @max ORDER BY Id");
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

    [Fact]
    public async Task QuotedColumns_RoundTrip()
    {
        var fx = new SqliteFixture();
        await fx.InitializeAsync().ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            // Mixed-case names, one of them a reserved word: only a quoted reference parses.
            await fx.ExecuteDdlAsync("CREATE TABLE \"Orders\" (\"Id\" INTEGER PRIMARY KEY, \"Order\" TEXT, \"Name\" TEXT)").ConfigureAwait(false);
            var repo = new ColumnPlaceholderRepo(fx.Connection, "\"Orders\"", quoteColumns: true);

            (await repo.InsertOrderAsync(1, "first", CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
            (await repo.InsertNameAsync(2, "bolt", CancellationToken.None).ConfigureAwait(false)).Should().Be(1);

            (await repo.GetOrderAsync(1, CancellationToken.None).ConfigureAwait(false)).Should().Be("first");
            (await repo.GetNameAsync(2, CancellationToken.None).ConfigureAwait(false)).Should().Be("bolt");
        }
    }

    [Fact]
    public async Task BareColumns_RoundTrip()
    {
        var fx = await SeededAsync("Widgets").ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var repo = new ColumnPlaceholderRepo(fx.Connection, "Widgets", quoteColumns: false);

            (await repo.InsertNameAsync(1, "bolt", CancellationToken.None).ConfigureAwait(false)).Should().Be(1);

            (await repo.GetNameAsync(1, CancellationToken.None).ConfigureAwait(false)).Should().Be("bolt");
        }
    }

    [Fact]
    public async Task ReservedWordColumn_BareFails_WhereQuotedWorks()
    {
        var fx = new SqliteFixture();
        await fx.InitializeAsync().ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            await fx.ExecuteDdlAsync("CREATE TABLE Orders (Id INTEGER PRIMARY KEY, \"Order\" TEXT NOT NULL)").ConfigureAwait(false);
            var bare = new ColumnPlaceholderRepo(fx.Connection, "Orders", quoteColumns: false);
            var quoted = new ColumnPlaceholderRepo(fx.Connection, "Orders", quoteColumns: true);

            var act = async () => await bare.InsertOrderAsync(1, "x", CancellationToken.None).ConfigureAwait(false);
            await act.Should().ThrowAsync<SqliteException>().ConfigureAwait(false);

            (await quoted.InsertOrderAsync(1, "x", CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
        }
    }

    [Fact]
    public async Task EqualButDistinctStringReference_ResolvesToSameSql()
    {
        var fx = await SeededAsync("Widgets").ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var first = new TablePlaceholderRepo(fx.Connection, "Widgets");
            var second = new TablePlaceholderRepo(fx.Connection, new string("Widgets".AsSpan()));
            await first.InsertAsync(1, "bolt", CancellationToken.None).ConfigureAwait(false);

            (await second.CountAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
            (await first.CountAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
            // The distinct-address instance filled the memo first; the original address still resolves.
            (await first.CountByIdAsync(1, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
            (await second.CountByIdAsync(1, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
        }
    }

    [Fact]
    public async Task ChangedMember_IsHonouredOnNextCall()
    {
        var fx = await SeededAsync("Widgets", "Gadgets").ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var widgets = new TablePlaceholderRepo(fx.Connection, "Widgets");
            var gadgets = new TablePlaceholderRepo(fx.Connection, "Gadgets");
            await widgets.InsertAsync(1, "bolt", CancellationToken.None).ConfigureAwait(false);
            await gadgets.InsertAsync(1, "gear", CancellationToken.None).ConfigureAwait(false);
            await gadgets.InsertAsync(2, "lever", CancellationToken.None).ConfigureAwait(false);
            var repo = new ChangingTableRepo(fx.Connection) { Table = "Widgets" };

            (await repo.CountAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
            repo.Table = "Gadgets";
            (await repo.CountAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(2);
        }
    }

    [Fact]
    public async Task NullOrBlankMember_ThrowsEvenWhenMemoIsFilled()
    {
        var fx = await SeededAsync("Widgets").ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var filled = new ChangingTableRepo(fx.Connection) { Table = "Widgets" };
            (await filled.CountAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(0);

            var nullRepo = new ChangingTableRepo(fx.Connection) { Table = null };
            var blankRepo = new ChangingTableRepo(fx.Connection) { Table = "  " };

            var nullAct = async () => await nullRepo.CountAsync(CancellationToken.None).ConfigureAwait(false);
            var blankAct = async () => await blankRepo.CountAsync(CancellationToken.None).ConfigureAwait(false);
            await nullAct.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
            await blankAct.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task QuoteFlagFlippedBetweenInstances_GetsOwnQuoting()
    {
        var fx = new SqliteFixture();
        await fx.InitializeAsync().ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            await fx.ExecuteDdlAsync("CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Name TEXT)").ConfigureAwait(false);
            var quoted = new ColumnPlaceholderRepo(fx.Connection, "Orders", quoteColumns: true);
            var bare = new ColumnPlaceholderRepo(fx.Connection, "Orders", quoteColumns: false);

            await quoted.InsertNameAsync(1, "bolt", CancellationToken.None).ConfigureAwait(false);
            await bare.InsertNameAsync(2, "gear", CancellationToken.None).ConfigureAwait(false);

            (await quoted.GetNameAsync(2, CancellationToken.None).ConfigureAwait(false)).Should().Be("gear");
            (await bare.GetNameAsync(1, CancellationToken.None).ConfigureAwait(false)).Should().Be("bolt");
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
