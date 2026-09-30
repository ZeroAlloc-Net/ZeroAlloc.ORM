using System.Data.Async;
using System.Globalization;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.Migrations;

// #306 — migration versions are scoped by IMigrationSource.Name, so two sources that both
// number from 1 apply to one database. A history table from before scoping is upgraded in place
// and its rows are assigned to the source that wrote it. The same scenarios run on every
// built-in dialect; the subclasses supply the connection and the pre-scoping table layout.
public abstract class MigrationSourceScopeTests : IAsyncLifetime
{
    private static readonly string[] TestTables = ["a_table", "b_table", "c_table"];

    protected abstract IAsyncDbConnection Connection { get; }

    protected abstract IScopedMigrationDialect Dialect { get; }

    // The history table exactly as the dialect created it before #306.
    protected abstract string UnscopedHistoryTableSql { get; }

    // A row as the runner recorded it before #306.
    protected abstract string UnscopedHistoryRowSql(int version, string name);

    protected virtual ValueTask ConnectAsync() => ValueTask.CompletedTask;

    protected virtual ValueTask DisconnectAsync() => ValueTask.CompletedTask;

    public async ValueTask InitializeAsync()
    {
        await ConnectAsync().ConfigureAwait(false);

        // The server fixtures are shared by the tests of a class, so start from an empty database.
        foreach (var table in TestTables.Append("__zaorm_migrations"))
            await ExecuteAsync($"DROP TABLE IF EXISTS {table}").ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private static readonly Migration CreateA = new(1, "create_a", "CREATE TABLE a_table (id INT NOT NULL PRIMARY KEY)");
    private static readonly Migration AlterA = new(2, "alter_a", "ALTER TABLE a_table ADD extra INT");
    private static readonly Migration CreateC = new(3, "create_c", "CREATE TABLE c_table (id INT NOT NULL PRIMARY KEY)");
    private static readonly Migration CreateB = new(1, "create_b", "CREATE TABLE b_table (id INT NOT NULL PRIMARY KEY)");

    private static TestSource LibraryA(params Migration[] migrations) => new("LibraryA", migrations);

    private static TestSource LibraryB(params Migration[] migrations) => new("LibraryB", migrations);

    [Fact]
    public async Task Two_sources_numbered_from_1_apply_to_one_database()
    {
        var a = await RunAsync(LibraryA(CreateA, AlterA)).ConfigureAwait(false);
        var b = await RunAsync(LibraryB(CreateB)).ConfigureAwait(false);

        Assert.Equal([1, 2], a.Select(m => m.Version));
        Assert.Equal([1], b.Select(m => m.Version));
        Assert.Equal(
            ["LibraryA:1:create_a", "LibraryA:2:alter_a", "LibraryB:1:create_b"],
            await ReadHistoryAsync().ConfigureAwait(false));

        // Each source replays silently.
        Assert.Empty(await RunAsync(LibraryA(CreateA, AlterA)).ConfigureAwait(false));
        Assert.Empty(await RunAsync(LibraryB(CreateB)).ConfigureAwait(false));
    }

    [Fact]
    public async Task A_version_collision_within_one_source_still_throws()
    {
        await RunAsync(LibraryA(CreateA)).ConfigureAwait(false);

        var ex = await Assert.ThrowsAsync<ZeroAllocOrmMigrationConflictException>(
            () => RunAsync(LibraryA(new Migration(1, "create_other", "CREATE TABLE c_table (id INT NOT NULL)")))).ConfigureAwait(false);

        Assert.Contains("create_a", ex.Message, StringComparison.Ordinal);
        Assert.False(await TableExistsAsync("c_table").ConfigureAwait(false));
    }

    [Fact]
    public async Task Unscoped_history_is_upgraded_in_place_and_assigned_to_the_source_that_wrote_it()
    {
        await WriteUnscopedHistoryAsync((1, "create_a"), (2, "alter_a")).ConfigureAwait(false);
        await ExecuteAsync(CreateA.Sql).ConfigureAwait(false);
        await ExecuteAsync(AlterA.Sql).ConfigureAwait(false);

        // Library A wrote the history; it now has a third migration.
        var applied = await RunAsync(LibraryA(CreateA, AlterA, CreateC)).ConfigureAwait(false);

        Assert.Equal([3], applied.Select(m => m.Version));
        Assert.Equal(
            ["LibraryA:1:create_a", "LibraryA:2:alter_a", "LibraryA:3:create_c"],
            await ReadHistoryAsync().ConfigureAwait(false));

        // The key is now (source, version): another source's version 1 applies beside A's.
        Assert.Equal([1], (await RunAsync(LibraryB(CreateB)).ConfigureAwait(false)).Select(m => m.Version));

        // And the upgrade does not run again.
        Assert.Empty(await RunAsync(LibraryA(CreateA, AlterA, CreateC)).ConfigureAwait(false));
    }

    [Fact]
    public async Task Upgrade_keeps_the_recorded_applied_at()
    {
        await WriteUnscopedHistoryAsync((1, "create_a")).ConfigureAwait(false);
        await ExecuteAsync(CreateA.Sql).ConfigureAwait(false);
        var before = await ScalarTextAsync("SELECT applied_at FROM __zaorm_migrations WHERE version = 1").ConfigureAwait(false);

        await RunAsync(LibraryA(CreateA)).ConfigureAwait(false);

        Assert.Equal(before, await ScalarTextAsync("SELECT applied_at FROM __zaorm_migrations WHERE version = 1").ConfigureAwait(false));
    }

    [Fact]
    public async Task Empty_unscoped_history_is_upgraded()
    {
        await ExecuteAsync(UnscopedHistoryTableSql).ConfigureAwait(false);

        Assert.Equal([1], (await RunAsync(LibraryA(CreateA)).ConfigureAwait(false)).Select(m => m.Version));
        Assert.Equal([1], (await RunAsync(LibraryB(CreateB)).ConfigureAwait(false)).Select(m => m.Version));
    }

    [Fact]
    public async Task Unscoped_history_of_another_source_is_left_unchanged_and_reported()
    {
        // Library A wrote the history, but library B runs first after the upgrade to scoping.
        await WriteUnscopedHistoryAsync((1, "create_a")).ConfigureAwait(false);
        await ExecuteAsync(CreateA.Sql).ConfigureAwait(false);

        var ex = await Assert.ThrowsAsync<ZeroAllocOrmMigrationConflictException>(
            () => RunAsync(LibraryB(CreateB))).ConfigureAwait(false);

        Assert.Contains("'LibraryB'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1 'create_a'", ex.Message, StringComparison.Ordinal);
        Assert.False(ex.Data.Contains(MigrationRunner.RollbackExceptionDataKey));
        Assert.False(await TableExistsAsync("b_table").ConfigureAwait(false));
        await AssertStillUnscopedAsync(["1:create_a"]).ConfigureAwait(false);

        // Running the source that wrote it upgrades the table; then B applies.
        Assert.Empty(await RunAsync(LibraryA(CreateA)).ConfigureAwait(false));
        Assert.Equal([1], (await RunAsync(LibraryB(CreateB)).ConfigureAwait(false)).Select(m => m.Version));
    }

    [Fact]
    public async Task Unscoped_history_that_matches_none_of_the_source_is_left_unchanged_and_reported()
    {
        await WriteUnscopedHistoryAsync((5, "something_else")).ConfigureAwait(false);

        var ex = await Assert.ThrowsAsync<ZeroAllocOrmMigrationConflictException>(
            () => RunAsync(LibraryA(CreateA))).ConfigureAwait(false);

        Assert.Contains("1 of its 1 rows", ex.Message, StringComparison.Ordinal);
        Assert.False(await TableExistsAsync("a_table").ConfigureAwait(false));
        await AssertStillUnscopedAsync(["5:something_else"]).ConfigureAwait(false);
    }

    [Fact]
    public async Task Unscoped_history_shared_by_two_sources_is_left_unchanged_and_reported()
    {
        // Before scoping, two sources could share the table with distinct version numbers.
        // Assigning library B's row to library A would make B apply its migration again, so the
        // upgrade is refused while any row is not one of the running source's migrations.
        await WriteUnscopedHistoryAsync((1, "create_a"), (1000, "create_b")).ConfigureAwait(false);
        await ExecuteAsync(CreateA.Sql).ConfigureAwait(false);
        await ExecuteAsync(CreateB.Sql).ConfigureAwait(false);

        var ex = await Assert.ThrowsAsync<ZeroAllocOrmMigrationConflictException>(
            () => RunAsync(LibraryA(CreateA, CreateC))).ConfigureAwait(false);

        Assert.Contains("1 of its 2 rows", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1000 'create_b'", ex.Message, StringComparison.Ordinal);
        Assert.False(await TableExistsAsync("c_table").ConfigureAwait(false));
        await AssertStillUnscopedAsync(["1:create_a", "1000:create_b"]).ConfigureAwait(false);
    }

    // --- helpers -----------------------------------------------------------

    private async Task<IReadOnlyList<Migration>> RunAsync(IMigrationSource source)
        => await new MigrationRunner(Connection, source, Dialect).RunAsync(CancellationToken.None).ConfigureAwait(false);

    private async Task WriteUnscopedHistoryAsync(params (int Version, string Name)[] rows)
    {
        await ExecuteAsync(UnscopedHistoryTableSql).ConfigureAwait(false);
        foreach (var (version, name) in rows)
            await ExecuteAsync(UnscopedHistoryRowSql(version, name)).ConfigureAwait(false);
    }

    private async Task AssertStillUnscopedAsync(string[] expectedRows)
    {
        var unscoped = await ScalarTextAsync(Dialect.SelectUnscopedHistorySql).ConfigureAwait(false);
        Assert.NotEqual("0", unscoped, StringComparer.Ordinal);
        Assert.Equal(
            expectedRows,
            await ReadRowsAsync("SELECT version, name FROM __zaorm_migrations ORDER BY version").ConfigureAwait(false),
            StringComparer.Ordinal);
    }

    private Task<List<string>> ReadHistoryAsync()
        => ReadRowsAsync("SELECT source, version, name FROM __zaorm_migrations ORDER BY source, version");

    private async Task<List<string>> ReadRowsAsync(string sql)
    {
        var rows = new List<string>();
        var cmd = Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            var reader = await cmd.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
            await using (((IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
            {
                while (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    var fields = new string[reader.FieldCount];
                    for (var i = 0; i < fields.Length; i++)
                        fields[i] = Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)!;
                    rows.Add(string.Join(':', fields));
                }
            }
        }
        return rows;
    }

    private async Task<string?> ScalarTextAsync(string sql)
    {
        var cmd = Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            var value = await cmd.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
            return value switch
            {
                null or DBNull => null,
                DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
                DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture),
            };
        }
    }

    private async Task<bool> TableExistsAsync(string table)
    {
        try
        {
            await ScalarTextAsync($"SELECT COUNT(*) FROM {table}").ConfigureAwait(false);
            return true;
        }
        catch (System.Data.Common.DbException)
        {
            return false;
        }
    }

    protected async Task ExecuteAsync(string sql)
    {
        var cmd = Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private sealed class TestSource(string name, Migration[] migrations) : IMigrationSource
    {
        public string Name => name;

        public IReadOnlyList<Migration> GetMigrations() => migrations;
    }
}
