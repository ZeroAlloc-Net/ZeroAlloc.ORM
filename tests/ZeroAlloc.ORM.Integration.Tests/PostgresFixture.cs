using Npgsql;
using System.Data.Async;
using System.Data.Async.Adapters;
using Testcontainers.PostgreSql;

namespace ZeroAlloc.ORM.Integration.Tests;

/// <summary>
/// Per-test Postgres connection backed by Testcontainers — boots a real
/// `postgres:16-alpine` container, opens an NpgsqlConnection wrapped via
/// AdoNet.Async, and tears the container down on disposal. Mirrors
/// <see cref="SqliteFixture"/>'s shape (manual <c>InitializeAsync</c>,
/// <see cref="IAsyncDisposable"/>) so tests can keep the existing
/// <c>await using (fx.ConfigureAwait(false))</c> pattern.
/// </summary>
/// <remarks>
/// <para>
/// Why not xUnit's <c>IAsyncLifetime</c>? The integration suite's existing
/// per-test fixture pattern instantiates fresh connections per <c>[Fact]</c>
/// so each test owns its schema. <c>IAsyncLifetime</c> needs an
/// <c>IClassFixture&lt;T&gt;</c> hook on every test class, which couples
/// schema-cleanup discipline to xUnit's collection model. Sticking with
/// <see cref="IAsyncDisposable"/> keeps the call-site identical to
/// <see cref="SqliteFixture"/>.
/// </para>
/// <para>
/// No Docker auto-skip. Testcontainers throws a clear container-start
/// error when Docker is unreachable — tests fail loudly so a missing
/// runtime doesn't silently shrink coverage. CI handles Docker availability
/// via runner choice (Phase A.5).
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container;
    private NpgsqlConnection? _raw;
    private NpgsqlDataSource? _dataSource;
    private readonly bool _slim;

    public IAsyncDbConnection Connection { get; private set; } = null!;

    // Internal — only the streaming "second-connection" test inside this
    // assembly reaches for the raw connection string. Demoted from public to
    // keep the fixture's surface minimal (post-review Fix 8).
    internal string ConnectionString => _container.GetConnectionString();

    public PostgresFixture() : this(slim: false)
    {
    }

    private PostgresFixture(bool slim)
    {
        _slim = slim;
        // Testcontainers 4.x requires passing the image to the builder ctor
        // (the parameterless overload is obsolete as of 4.10+).
        _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    }

    /// <summary>
    /// Convenience factory that constructs the fixture and awaits
    /// <see cref="InitializeAsync"/> in one call so test sites can write
    /// <c>await using var fx = await PostgresFixture.CreateAndInitializeAsync()</c>
    /// instead of the two-step ctor + initialize dance.
    /// </summary>
    public static async Task<PostgresFixture> CreateAndInitializeAsync()
    {
        var fx = new PostgresFixture();
        await fx.InitializeAsync().ConfigureAwait(false);
        return fx;
    }

    /// <summary>
    /// Like <see cref="CreateAndInitializeAsync"/>, but opens the connection from
    /// <c>new NpgsqlSlimDataSourceBuilder(cs).Build()</c>. The slim builder
    /// registers no optional type handlers, so array support has to be switched on explicitly.
    /// </summary>
    public static async Task<PostgresFixture> CreateSlimAndInitializeAsync()
    {
        var fx = new PostgresFixture(slim: true);
        await fx.InitializeAsync().ConfigureAwait(false);
        return fx;
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        if (_slim)
        {
            _dataSource = new NpgsqlSlimDataSourceBuilder(_container.GetConnectionString()).EnableArrays().Build();
            _raw = _dataSource.CreateConnection();
        }
        else
        {
            _raw = new NpgsqlConnection(_container.GetConnectionString());
        }
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
        if (_raw is not null)
        {
            await _raw.DisposeAsync().ConfigureAwait(false);
        }
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync().ConfigureAwait(false);
        }
        await _container.DisposeAsync().ConfigureAwait(false);
    }
}
