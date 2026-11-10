using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace ZeroAlloc.ORM.Integration.Tests.TestDoubles;

// #464 — a DbConnection whose CanCreateBatch is true, so generated code takes its IAsyncDbBatch
// branch, which Sqlite never does. Its batch executes nothing: it records the CommandText of each
// batch command in the order the commands were added, and answers ExecuteReader with the result
// sets the test supplies. CreateCommand throws, so a test also proves the joined path is not taken.
internal sealed class RecordingBatchDbConnection(params TableResult[] results) : DbConnection
{
    private readonly TableResult[] _results = results;

    public List<string> BatchCommandTexts { get; } = [];

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;

    public override string Database => "recording-batch";

    public override string DataSource => "recording-batch";

    public override string ServerVersion => "0";

    public override ConnectionState State => ConnectionState.Open;

    public override bool CanCreateBatch => true;

    public override void ChangeDatabase(string databaseName) { }

    public override void Close() { }

    public override void Open() { }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        throw new NotSupportedException();

    protected override DbCommand CreateDbCommand() => throw new NotSupportedException("The batch path creates no command.");

    protected override DbBatch CreateDbBatch() => new RecordingBatch(this);

    private sealed class RecordingBatch(RecordingBatchDbConnection owner) : DbBatch
    {
        private readonly RecordingBatchCommandCollection _commands = new();

        public override int Timeout { get; set; }

        protected override DbBatchCommandCollection DbBatchCommands => _commands;

        protected override DbConnection? DbConnection { get; set; }

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override object? ExecuteScalar() => throw new NotSupportedException();

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override void Prepare() { }

        public override Task PrepareAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        protected override DbBatchCommand CreateDbBatchCommand() => new RecordingBatchCommand();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            foreach (var command in _commands) owner.BatchCommandTexts.Add(command.CommandText);
            return new TableDataReader(owner._results);
        }

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior, CancellationToken cancellationToken) =>
            Task.FromResult(ExecuteDbDataReader(behavior));
    }

    private sealed class RecordingBatchCommandCollection : DbBatchCommandCollection
    {
        private readonly List<DbBatchCommand> _items = [];

        public override int Count => _items.Count;

        public override bool IsReadOnly => false;

        public override void Add(DbBatchCommand item) => _items.Add(item);

        public override void Clear() => _items.Clear();

        public override bool Contains(DbBatchCommand item) => _items.Contains(item);

        public override void CopyTo(DbBatchCommand[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public override IEnumerator<DbBatchCommand> GetEnumerator() => _items.GetEnumerator();

        public override int IndexOf(DbBatchCommand item) => _items.IndexOf(item);

        public override void Insert(int index, DbBatchCommand item) => _items.Insert(index, item);

        public override bool Remove(DbBatchCommand item) => _items.Remove(item);

        public override void RemoveAt(int index) => _items.RemoveAt(index);

        protected override DbBatchCommand GetBatchCommand(int index) => _items[index];

        protected override void SetBatchCommand(int index, DbBatchCommand batchCommand) => _items[index] = batchCommand;
    }

#pragma warning disable CA1001 // The SqliteCommand holds no connection or handle; it only supplies a parameter collection.
    private sealed class RecordingBatchCommand : DbBatchCommand
    {
#pragma warning disable CA2213 // Only the parameter collection is used.
        private readonly SqliteCommand _parameters = new();
#pragma warning restore CA2213

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override int RecordsAffected => 0;

        protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;

        public override bool CanCreateParameter => true;

        public override DbParameter CreateParameter() => _parameters.CreateParameter();
    }
#pragma warning restore CA1001
}
