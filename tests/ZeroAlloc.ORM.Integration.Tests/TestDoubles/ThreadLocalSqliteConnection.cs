using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace ZeroAlloc.ORM.Integration.Tests.TestDoubles;

// #464 — one DbConnection object that hands each thread its own SqliteConnection to the same
// shared-cache in-memory database. A SqliteConnection is not safe to use from two threads at
// once, so this lets one repository instance take truly concurrent calls. It always reports
// Open, and opens a thread's connection when that thread first creates a command. Each command
// it makes runs on the calling thread's connection; see ThreadBoundSqliteCommand.
internal sealed class ThreadLocalSqliteConnection(string connectionString) : DbConnection
{
    private readonly ThreadLocal<SqliteConnection> _perThread = new(
        () =>
        {
            var c = new SqliteConnection(connectionString);
            c.Open();
            return c;
        },
        trackAllValues: true);

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString
    {
        get => connectionString;
        set => throw new NotSupportedException();
    }

    public override string Database => "main";

    public override string DataSource => connectionString;

    public override string ServerVersion => _perThread.Value!.ServerVersion;

    public override ConnectionState State => ConnectionState.Open;

    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

    public override void Open()
    {
    }

    public override void Close()
    {
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

    protected override DbCommand CreateDbCommand() => new ThreadBoundSqliteCommand(_perThread.Value!.CreateCommand(), this);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var c in _perThread.Values)
                c.Dispose();
            _perThread.Dispose();
        }
        base.Dispose(disposing);
    }
}
