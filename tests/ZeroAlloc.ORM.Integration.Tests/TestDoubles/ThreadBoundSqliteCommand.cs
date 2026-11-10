using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace ZeroAlloc.ORM.Integration.Tests.TestDoubles;

// #464 — a SqliteCommand on one thread's connection, presented as a command of the
// ThreadLocalSqliteConnection that made it. The AdoNet.Async adapter sets a command's Connection
// to the connection it wraps, which a SqliteCommand would reject; this command keeps its own.
internal sealed class ThreadBoundSqliteCommand(SqliteCommand inner, DbConnection owner) : DbCommand
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string CommandText
    {
        get => inner.CommandText;
        set => inner.CommandText = value;
    }

    public override int CommandTimeout
    {
        get => inner.CommandTimeout;
        set => inner.CommandTimeout = value;
    }

    public override CommandType CommandType
    {
        get => inner.CommandType;
        set => inner.CommandType = value;
    }

    public override bool DesignTimeVisible
    {
        get => inner.DesignTimeVisible;
        set => inner.DesignTimeVisible = value;
    }

    public override UpdateRowSource UpdatedRowSource
    {
        get => inner.UpdatedRowSource;
        set => inner.UpdatedRowSource = value;
    }

    protected override DbConnection? DbConnection
    {
        get => owner;
        set { }
    }

    protected override DbParameterCollection DbParameterCollection => inner.Parameters;

    protected override DbTransaction? DbTransaction
    {
        get => null;
        set
        {
            if (value is not null) throw new NotSupportedException();
        }
    }

    public override void Cancel() => inner.Cancel();

    public override int ExecuteNonQuery() => inner.ExecuteNonQuery();

    public override object? ExecuteScalar() => inner.ExecuteScalar();

    public override void Prepare() => inner.Prepare();

    protected override DbParameter CreateDbParameter() => inner.CreateParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => inner.ExecuteReader(behavior);

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
