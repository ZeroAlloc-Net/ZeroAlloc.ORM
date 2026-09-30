using System.Data.Async;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.Migrations;

public sealed class PostgresMigrationSourceScopeTests(PostgresMigrationSourceScopeTests.Server server)
    : MigrationSourceScopeTests, IClassFixture<PostgresMigrationSourceScopeTests.Server>
{
    protected override IAsyncDbConnection Connection => server.Fixture.Connection;

    protected override IScopedMigrationDialect Dialect { get; } = new PostgresMigrationDialect();

    protected override string UnscopedHistoryTableSql =>
        "CREATE TABLE __zaorm_migrations (version INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW())";

    protected override string UnscopedHistoryRowSql(int version, string name)
        => $"INSERT INTO __zaorm_migrations (version, name) VALUES ({version}, '{name}')";

    public sealed class Server : IAsyncLifetime
    {
        public PostgresFixture Fixture { get; } = new();

        public ValueTask InitializeAsync() => Fixture.InitializeAsync();

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }
}
