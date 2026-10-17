using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Async.Adapters;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.SqlServer;

/// <summary>
/// Pins the SQL text <see cref="EfCoreBaseline"/> sends to SQL Server. The text is provider
/// specific, so it is recorded through a fake connection rather than run against a server.
/// </summary>
public sealed class SqlServerEfCoreBaselineSqlTests
{
    [Fact]
    public async Task LatestMigrationIs_uses_bracketed_identifiers()
    {
        var fake = new ScalarRecordingConnection(1, "20261014090300_AddIndex");
        var asyncConnection = fake.AsAsync();
        await using (asyncConnection.ConfigureAwait(false))
        {
            var decision = await EfCoreBaseline.LatestMigrationIs("20261014090300_AddIndex", SqlDialect.SqlServer)
                .DecideAsync(asyncConnection, CancellationToken.None).ConfigureAwait(false);

            decision.Should().Be(BaselineDecision.MarkApplied);
        }

        fake.CommandTexts.Should().Equal(
            "SELECT CASE WHEN OBJECT_ID(N'[__EFMigrationsHistory]', N'U') IS NULL THEN 0 ELSE 1 END",
            "SELECT MAX([MigrationId]) FROM [__EFMigrationsHistory]");
    }

    [Fact]
    public async Task Missing_history_looks_for_the_table_in_other_schemas_through_sys_tables()
    {
        var fake = new ScalarRecordingConnection(0, "audit");
        var asyncConnection = fake.AsAsync();
        await using (asyncConnection.ConfigureAwait(false))
        {
            var act = () => EfCoreBaseline.LatestMigrationIs("20261014090300_AddIndex", SqlDialect.SqlServer)
                .DecideAsync(asyncConnection, CancellationToken.None);

            (await act.Should().ThrowAsync<ZeroAllocOrmMigrationConflictException>().ConfigureAwait(false))
                .Which.Message.Should().Contain("audit");
        }

        fake.CommandTexts.Should().HaveCount(2);
        fake.CommandTexts[1].Should().Be(
            "SELECT TOP 1 SCHEMA_NAME(schema_id) FROM sys.tables WHERE name = '__EFMigrationsHistory' ORDER BY schema_id");
    }

    // Returns the queued scalar results in order and records each CommandText.
    private sealed class ScalarRecordingConnection(params object[] results) : DbConnection
    {
        private int _next;

        public List<string> CommandTexts { get; } = [];

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "0";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new FakeCommand(this);

        private object Next(string commandText)
        {
            CommandTexts.Add(commandText);
            return results[_next++];
        }

        private sealed class FakeCommand(ScalarRecordingConnection owner) : DbCommand
        {
            private readonly SqliteCommand _parameters = new();

            [System.Diagnostics.CodeAnalysis.AllowNull]
            public override string CommandText { get; set; } = string.Empty;
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; } = CommandType.Text;
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; }
            protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;
            protected override DbTransaction? DbTransaction { get; set; }

            public override void Cancel() { }
            public override int ExecuteNonQuery() => throw new NotSupportedException();
            public override object? ExecuteScalar() => owner.Next(CommandText);
            public override void Prepare() { }
            protected override DbParameter CreateDbParameter() => _parameters.CreateParameter();
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
                => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) _parameters.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
