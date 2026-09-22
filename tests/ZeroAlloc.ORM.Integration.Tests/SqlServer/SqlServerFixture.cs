using System.Data.Async;
using System.Data.Async.Adapters;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace ZeroAlloc.ORM.Integration.Tests.SqlServer;

/// <summary>
/// A real SQL Server 2022 in a container, so the dialect's SQL is exercised
/// against the engine rather than assumed correct.
/// </summary>
public sealed class SqlServerFixture : IAsyncDisposable
{
    private readonly MsSqlContainer _container;
    private SqlConnection? _raw;

    public IAsyncDbConnection Connection { get; private set; } = null!;

    public SqlServerFixture() => _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        _raw = new SqlConnection(_container.GetConnectionString());
        Connection = _raw.AsAsync();
        await Connection.OpenAsync().ConfigureAwait(false);
    }

    /// <summary>Opens a second, independent connection — needed to prove the apply-lock blocks.</summary>
    public async Task<IAsyncDbConnection> OpenSecondAsync()
    {
        var c = new SqlConnection(_container.GetConnectionString());
        var async = c.AsAsync();
        await async.OpenAsync().ConfigureAwait(false);
        return async;
    }

    public async ValueTask DisposeAsync()
    {
        if (_raw is not null) await _raw.DisposeAsync().ConfigureAwait(false);
        await _container.DisposeAsync().ConfigureAwait(false);
    }
}
