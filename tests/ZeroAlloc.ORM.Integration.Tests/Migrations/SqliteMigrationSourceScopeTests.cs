using System.Data.Async;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.Migrations;

public sealed class SqliteMigrationSourceScopeTests : MigrationSourceScopeTests
{
    private SqliteFixture? _fx;

    protected override IAsyncDbConnection Connection => _fx!.Connection;

    protected override IScopedMigrationDialect Dialect { get; } = new SqliteMigrationDialect();

    protected override string UnscopedHistoryTableSql =>
        "CREATE TABLE __zaorm_migrations (version INTEGER PRIMARY KEY,name TEXT NOT NULL,applied_at TEXT NOT NULL)";

    protected override string UnscopedHistoryRowSql(int version, string name)
        => $"INSERT INTO __zaorm_migrations (version, name, applied_at) VALUES ({version}, '{name}', '2026-01-02T03:04:05.0000000Z')";

    protected override async ValueTask ConnectAsync()
    {
        _fx = new SqliteFixture();
        await _fx.InitializeAsync().ConfigureAwait(false);
    }

    protected override async ValueTask DisconnectAsync()
    {
        if (_fx is not null)
            await _fx.DisposeAsync().ConfigureAwait(false);
    }
}
