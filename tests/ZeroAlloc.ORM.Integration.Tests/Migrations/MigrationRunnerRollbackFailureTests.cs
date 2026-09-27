using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Async;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.ORM.Integration.Tests.Migrations;

/// <summary>
/// Issue #294 — when a migration fails and the rollback of its transaction fails too,
/// the runner must still rethrow the original exception unchanged, and must attach the
/// rollback failure to it under <see cref="MigrationRunner.RollbackExceptionDataKey"/>
/// instead of discarding it.
///
/// Runs against a real in-memory Sqlite database through a thin decorator connection
/// that lets a test make one migration body throw a chosen exception and make the
/// transaction's rollback throw after the real rollback has completed.
/// </summary>
public class MigrationRunnerRollbackFailureTests
{
    private const string FailingBodySql = "-- body fails";

    [Fact]
    public async Task Failed_rollback_is_attached_to_the_original_exception()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);

        var bodyFailure = new InvalidOperationException("migration body failed");
        var rollbackFailure = new TimeoutException("rollback failed");
        var connection = new FaultInjectingConnection(fixture.Connection, bodyFailure, rollbackFailure);

        var runner = new MigrationRunner(connection, Source(), new SqliteMigrationDialect());

        var act = async () => await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);
        var thrown = (await act.Should().ThrowExactlyAsync<InvalidOperationException>().ConfigureAwait(false))
            .WithMessage("migration body failed")
            .Which;

        thrown.Should().BeSameAs(bodyFailure);
        thrown.Data[MigrationRunner.RollbackExceptionDataKey].Should().BeSameAs(rollbackFailure);
        connection.RollbackAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Successful_rollback_adds_no_data_entry()
    {
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);

        var bodyFailure = new InvalidOperationException("migration body failed");
        var connection = new FaultInjectingConnection(fixture.Connection, bodyFailure, rollbackFailure: null);

        var runner = new MigrationRunner(connection, Source(), new SqliteMigrationDialect());

        var act = async () => await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);
        var thrown = (await act.Should().ThrowExactlyAsync<InvalidOperationException>().ConfigureAwait(false))
            .WithMessage("migration body failed")
            .Which;

        thrown.Should().BeSameAs(bodyFailure);
        thrown.Data.Contains(MigrationRunner.RollbackExceptionDataKey).Should().BeFalse();
        connection.RollbackAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Read_only_exception_data_does_not_replace_the_original_exception()
    {
        // Exception.Data is virtual; an exception type can expose a read-only dictionary.
        // Writing to it would throw NotSupportedException from inside the catch handler,
        // replacing the migration failure the caller needs to see.
        await using var fixture = new SqliteFixture();
        await fixture.InitializeAsync().ConfigureAwait(false);

        var bodyFailure = new ReadOnlyDataException("migration body failed");
        var rollbackFailure = new TimeoutException("rollback failed");
        var connection = new FaultInjectingConnection(fixture.Connection, bodyFailure, rollbackFailure);

        var runner = new MigrationRunner(connection, Source(), new SqliteMigrationDialect());

        var act = async () => await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);
        (await act.Should().ThrowExactlyAsync<ReadOnlyDataException>().ConfigureAwait(false))
            .WithMessage("migration body failed")
            .Which.Should().BeSameAs(bodyFailure);
    }

    private static ListMigrationSource Source() => new(new[]
    {
        new Migration(1, "create_users", "CREATE TABLE users (id INTEGER PRIMARY KEY)"),
        new Migration(2, "broken", FailingBodySql),
    });

    private sealed class ListMigrationSource : IMigrationSource
    {
        private readonly IReadOnlyList<Migration> _migrations;
        public ListMigrationSource(IEnumerable<Migration> migrations) => _migrations = migrations.ToList();
        public IReadOnlyList<Migration> GetMigrations() => _migrations;
    }

    private sealed class ReadOnlyDataException : Exception
    {
        private static readonly IDictionary Empty =
            new ReadOnlyDictionary<object, object?>(new Dictionary<object, object?>());

        public ReadOnlyDataException() { }

        public ReadOnlyDataException(string message) : base(message) { }

        public ReadOnlyDataException(string message, Exception innerException) : base(message, innerException) { }

        public override IDictionary Data => Empty;
    }

    /// <summary>
    /// Delegates to a real connection. A command whose text is <see cref="FailingBodySql"/>
    /// throws the configured body failure; a transaction's rollback performs the real
    /// rollback and then throws the configured rollback failure, if any.
    /// </summary>
    private sealed class FaultInjectingConnection : AsyncDbConnection
    {
        private readonly IAsyncDbConnection _inner;
        private readonly Exception _bodyFailure;
        private readonly Exception? _rollbackFailure;

        public FaultInjectingConnection(IAsyncDbConnection inner, Exception bodyFailure, Exception? rollbackFailure)
        {
            _inner = inner;
            _bodyFailure = bodyFailure;
            _rollbackFailure = rollbackFailure;
        }

        public int RollbackAttempts { get; private set; }

        public override string ConnectionString
        {
            get => _inner.ConnectionString;
            set => _inner.ConnectionString = value;
        }

        public override string Database => _inner.Database;
        public override ConnectionState State => _inner.State;
        public override int ConnectionTimeout => _inner.ConnectionTimeout;

        protected override async ValueTask<IAsyncDbTransaction> BeginDbTransactionAsync(
            IsolationLevel isolationLevel, CancellationToken cancellationToken)
        {
            var inner = await _inner.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
            return new FaultInjectingTransaction(this, inner);
        }

        protected override ValueTask OpenCoreAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);
        protected override ValueTask CloseCoreAsync() => _inner.CloseAsync();

        protected override ValueTask ChangeDatabaseCoreAsync(string databaseName, CancellationToken cancellationToken)
            => _inner.ChangeDatabaseAsync(databaseName, cancellationToken);

        protected override IAsyncDbCommand CreateDbCommand() => new FaultInjectingCommand(this, _inner.CreateCommand());

        private sealed class FaultInjectingTransaction : AsyncDbTransaction
        {
            private readonly FaultInjectingConnection _owner;

            public FaultInjectingTransaction(FaultInjectingConnection owner, IAsyncDbTransaction inner)
            {
                _owner = owner;
                Inner = inner;
            }

            public IAsyncDbTransaction Inner { get; }
            public override IAsyncDbConnection Connection => _owner;
            public override IsolationLevel IsolationLevel => Inner.IsolationLevel;

            protected override ValueTask CommitCoreAsync(CancellationToken cancellationToken) => Inner.CommitAsync(cancellationToken);

            protected override async ValueTask RollbackCoreAsync(CancellationToken cancellationToken)
            {
                _owner.RollbackAttempts++;
                await Inner.RollbackAsync(cancellationToken).ConfigureAwait(false);
                if (_owner._rollbackFailure is not null)
                {
                    throw _owner._rollbackFailure;
                }
            }

            protected override async ValueTask DisposeAsyncCore()
            {
                await Inner.DisposeAsync().ConfigureAwait(false);
                await base.DisposeAsyncCore().ConfigureAwait(false);
            }
        }

        private sealed class FaultInjectingCommand : AsyncDbCommand
        {
            private readonly FaultInjectingConnection _owner;
            private readonly IAsyncDbCommand _inner;
            private IAsyncDbTransaction? _transaction;

            public FaultInjectingCommand(FaultInjectingConnection owner, IAsyncDbCommand inner)
            {
                _owner = owner;
                _inner = inner;
            }

            public override string CommandText
            {
                get => _inner.CommandText;
                set => _inner.CommandText = value;
            }

            public override int CommandTimeout
            {
                get => _inner.CommandTimeout;
                set => _inner.CommandTimeout = value;
            }

            public override CommandType CommandType
            {
                get => _inner.CommandType;
                set => _inner.CommandType = value;
            }

            public override IAsyncDbConnection? Connection
            {
                get => _owner;
                set => throw new NotSupportedException();
            }

            public override IAsyncDbTransaction? Transaction
            {
                get => _transaction;
                set
                {
                    // The provider command only accepts its own transaction type, so hand it
                    // the wrapped one.
                    _transaction = value;
                    _inner.Transaction = (value as FaultInjectingTransaction)?.Inner ?? value;
                }
            }

            public override IDataParameterCollection Parameters => _inner.Parameters;

            public override UpdateRowSource UpdatedRowSource
            {
                get => _inner.UpdatedRowSource;
                set => _inner.UpdatedRowSource = value;
            }

            public override IDbDataParameter CreateParameter() => _inner.CreateParameter();
            public override void Cancel() => _inner.Cancel();

            protected override ValueTask<IAsyncDataReader> ExecuteDbReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
                => _inner.ExecuteReaderAsync(behavior, cancellationToken);

            protected override ValueTask<int> ExecuteNonQueryCoreAsync(CancellationToken cancellationToken)
            {
                if (string.Equals(_inner.CommandText, FailingBodySql, StringComparison.Ordinal))
                {
                    throw _owner._bodyFailure;
                }

                return _inner.ExecuteNonQueryAsync(cancellationToken);
            }

            protected override ValueTask<object?> ExecuteScalarCoreAsync(CancellationToken cancellationToken)
                => _inner.ExecuteScalarAsync(cancellationToken);

            protected override ValueTask PrepareCoreAsync(CancellationToken cancellationToken) => _inner.PrepareAsync(cancellationToken);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
