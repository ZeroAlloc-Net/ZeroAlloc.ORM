using Npgsql;
using System.Data.Async;
using System.Data.Async.Adapters;
using Testcontainers.PostgreSql;

namespace ZeroAlloc.ORM.Benchmarks.Postgres;

// v0.7 Phase A.4 — Postgres backend fixture. Mirrors the shape of
// tests/ZeroAlloc.ORM.Integration.Tests/PostgresFixture.cs but lives in
// the Benchmarks project to avoid cross-referencing the integration-tests
// csproj (its IsTestProject=true setting would pollute `dotnet test` and
// the xunit + Testcontainers transitive deps would inflate the BDN
// assembly graph). Same `postgres:16-alpine` image, same NpgsqlConnection
// wrapped via AsAsync.
internal sealed class PostgresBenchFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container;
    private NpgsqlConnection? _raw;

    public IAsyncDbConnection Connection { get; private set; } = null!;
    public NpgsqlConnection RawConnection => _raw ?? throw new InvalidOperationException("Fixture not initialised.");

    public PostgresBenchFixture()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        _raw = new NpgsqlConnection(_container.GetConnectionString());
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
        await _container.DisposeAsync().ConfigureAwait(false);
    }
}
