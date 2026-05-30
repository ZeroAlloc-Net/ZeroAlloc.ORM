using Microsoft.Data.Sqlite;
using System.Data.Async;
using System.Data.Async.Adapters;

namespace ZeroAlloc.ORM.Integration.Tests;

/// <summary>
/// Per-test in-memory Sqlite connection wrapped via AdoNet.Async. The connection
/// is kept alive for the test's lifetime so the in-memory database is not GC'd
/// between commands. Tests dispose explicitly via `await using`.
/// </summary>
public sealed class SqliteFixture : IAsyncDisposable
{
    private readonly SqliteConnection _raw = new("Data Source=:memory:");

    public IAsyncDbConnection Connection { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Connection = _raw.AsAsync();
        await Connection.OpenAsync().ConfigureAwait(false);
    }

    public async ValueTask ExecuteDdlAsync(string sql)
    {
        var cmd = Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Connection is not null)
        {
            await Connection.DisposeAsync().ConfigureAwait(false);
        }
        await _raw.DisposeAsync().ConfigureAwait(false);
    }
}
