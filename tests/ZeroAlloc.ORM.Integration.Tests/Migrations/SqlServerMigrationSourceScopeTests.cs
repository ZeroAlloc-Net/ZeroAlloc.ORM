using System.Data.Async;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.Migrations;

public sealed class SqlServerMigrationSourceScopeTests(SqlServerMigrationSourceScopeTests.Server server)
    : MigrationSourceScopeTests, IClassFixture<SqlServerMigrationSourceScopeTests.Server>
{
    protected override IAsyncDbConnection Connection => server.Fixture.Connection;

    protected override IScopedMigrationDialect Dialect { get; } = new SqlServerMigrationDialect();

    protected override string UnscopedHistoryTableSql =>
        "CREATE TABLE __zaorm_migrations (version INT NOT NULL PRIMARY KEY, name NVARCHAR(400) NOT NULL, " +
        "applied_at DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET())";

    protected override string UnscopedHistoryRowSql(int version, string name)
        => $"INSERT INTO __zaorm_migrations (version, name) VALUES ({version}, N'{name}')";

    public sealed class Server : IAsyncLifetime
    {
        public SqlServer.SqlServerFixture Fixture { get; } = new();

        public ValueTask InitializeAsync() => Fixture.InitializeAsync();

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }
}
