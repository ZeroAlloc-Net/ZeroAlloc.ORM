using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace ZeroAlloc.ORM.Integration.Tests.TestDoubles;

// #326 — a DbConnection that executes nothing and records every command it is
// handed: the CommandText and the ParameterName of each bound parameter, in
// bind order. The BulkInsert SQL characterization tests run one repository per
// SqlDialect through it, so they pin the exact text and names the emitted
// chunk loop produces for SQL that only that dialect's provider could run,
// such as SQL Server's `OUTPUT INSERTED.Id`, without a live server.
//
// ExecuteNonQuery reports one affected row per VALUES tuple; ExecuteReader
// returns one int `Id` row per tuple, numbered on from the previous command, so
// both BulkInsert return shapes complete. The tuple count comes from the
// placeholders per row the test supplies. A test that reads instead supplies
// the reader each ExecuteReader returns.
internal sealed class RecordingDbConnection(int placeholdersPerRow, Func<DbDataReader>? reader = null) : DbConnection
{
    public RecordingDbConnection(Func<DbDataReader> reader)
        : this(placeholdersPerRow: 1, reader)
    {
    }

    private int _nextId = 1;

    public List<RecordedCommand> Commands { get; } = [];

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;

    public override string Database => "recording";

    public override string DataSource => "recording";

    public override string ServerVersion => "0";

    public override ConnectionState State => ConnectionState.Open;

    public override void ChangeDatabase(string databaseName) { }

    public override void Close() { }

    public override void Open() { }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        throw new NotSupportedException();

    protected override DbCommand CreateDbCommand() => new RecordingDbCommand(this);

    internal int Record(DbCommand command)
    {
        var names = new string[command.Parameters.Count];
        for (var i = 0; i < names.Length; i++)
        {
            names[i] = command.Parameters[i].ParameterName;
        }
        Commands.Add(new RecordedCommand(command.CommandText, names));
        return names.Length / placeholdersPerRow;
    }

    internal DbDataReader CreateReader(int rows)
    {
        if (reader is not null) return reader();
        var first = _nextId;
        _nextId += rows;
        return new IdReader(first, rows);
    }

    private sealed class RecordingDbCommand(RecordingDbConnection owner) : DbCommand
    {
        // Only the parameter collection is used: SqliteCommand's works without a
        // connection, so the recorder needs no DbParameterCollection of its own.
#pragma warning disable CA2213 // Disposed with this command.
        private readonly SqliteCommand _parameters = new();
#pragma warning restore CA2213

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

        public override int ExecuteNonQuery() => owner.Record(this);

        public override object? ExecuteScalar() => throw new NotSupportedException();

        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => _parameters.CreateParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            var rows = owner.Record(this);
            return owner.CreateReader(rows);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _parameters.Dispose();
            base.Dispose(disposing);
        }
    }
}
