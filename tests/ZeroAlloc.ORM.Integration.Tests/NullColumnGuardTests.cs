using System.Data.Async;

namespace ZeroAlloc.ORM.Integration.Tests;

// #249 — NullColumnGuardTestsBase on Sqlite. Microsoft.Data.Sqlite throws
// InvalidOperationException for a NULL read through GetInt32 or GetString; its
// message gives the ordinal but not the column name.
public sealed class NullColumnGuardTests : NullColumnGuardTestsBase
{
    private readonly SqliteFixture _fx = new();

    protected override IAsyncDbConnection Connection => _fx.Connection;

    protected override string CreateTableSql => """
        CREATE TABLE NullGuard (
            Id INTEGER PRIMARY KEY, Name TEXT, Quantity INTEGER, State INTEGER,
            Ref INTEGER, Amount NUMERIC, Currency TEXT)
        """;

    protected override ValueTask StartAsync() => _fx.InitializeAsync();

    protected override ValueTask StopAsync() => _fx.DisposeAsync();

    protected override Type ExpectedNullReadException => typeof(InvalidOperationException);
}
