using System.Data.Async;
using System.Linq;
using AwesomeAssertions;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.SqlServer;

/// <summary>Runs <see cref="EfCoreBaseline"/> against a real SQL Server.</summary>
public sealed class SqlServerEfCoreBaselineTests : IAsyncLifetime
{
    private const string EfHead = "20261014090300_AddNodeSelectionUniqueIndex";

    private readonly SqlServerFixture _fx = new();

    public ValueTask InitializeAsync() => _fx.InitializeAsync();

    public ValueTask DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task No_ef_table_returns_RunAll()
    {
        var decision = await DecideAsync().ConfigureAwait(false);

        decision.Should().Be(BaselineDecision.RunAll);
    }

    [Fact]
    public async Task Ef_table_at_the_expected_id_returns_MarkApplied()
    {
        await CreateEfHistoryAsync("dbo").ConfigureAwait(false);

        var decision = await DecideAsync().ConfigureAwait(false);

        decision.Should().Be(BaselineDecision.MarkApplied);
    }

    [Fact]
    public async Task Ef_table_at_a_different_id_throws()
    {
        await CreateEfHistoryAsync("dbo", "20200101000000_Older").ConfigureAwait(false);

        var act = () => DecideAsync();

        await act.Should().ThrowAsync<ZeroAllocOrmMigrationConflictException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task Ef_table_in_another_schema_throws_naming_the_schema()
    {
        await ExecuteAsync("CREATE SCHEMA audit").ConfigureAwait(false);
        await CreateEfHistoryAsync("audit").ConfigureAwait(false);

        var act = () => DecideAsync();

        (await act.Should().ThrowAsync<ZeroAllocOrmMigrationConflictException>().ConfigureAwait(false))
            .Which.Message.Should().Contain("audit");
    }

    private Task<BaselineDecision> DecideAsync()
        => EfCoreBaseline.LatestMigrationIs(EfHead, SqlDialect.SqlServer)
            .DecideAsync(_fx.Connection, CancellationToken.None);

    private async Task CreateEfHistoryAsync(string schema, string id = EfHead)
    {
        await ExecuteAsync(
            $"CREATE TABLE [{schema}].[__EFMigrationsHistory] ([MigrationId] NVARCHAR(150) NOT NULL PRIMARY KEY, "
            + "[ProductVersion] NVARCHAR(32) NOT NULL)").ConfigureAwait(false);
        await ExecuteAsync(
            $"INSERT INTO [{schema}].[__EFMigrationsHistory] VALUES (N'{id}', N'9.0.0')").ConfigureAwait(false);
    }

    private async Task ExecuteAsync(string sql)
    {
        var cmd = _fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
