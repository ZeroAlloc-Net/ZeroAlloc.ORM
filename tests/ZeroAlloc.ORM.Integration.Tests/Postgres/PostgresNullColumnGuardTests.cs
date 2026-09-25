using System.Data.Async;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// #249 — NullColumnGuardTestsBase on Postgres. Npgsql throws InvalidCastException
// for a NULL read through GetInt32 or GetString; it names the column but not
// the member, and it is not the exception the docs promise.
[Trait("Provider", "Postgres")]
public sealed class PostgresNullColumnGuardTests : NullColumnGuardTestsBase
{
    private readonly PostgresFixture _fx = new();

    protected override IAsyncDbConnection Connection => _fx.Connection;

    protected override string CreateTableSql => """
        CREATE TABLE NullGuard (
            Id INTEGER PRIMARY KEY, Name TEXT, Quantity INTEGER, State INTEGER,
            Ref INTEGER, Amount NUMERIC(18,2), Currency TEXT)
        """;

    protected override ValueTask StartAsync() => _fx.InitializeAsync();

    protected override ValueTask StopAsync() => _fx.DisposeAsync();

    protected override Type ExpectedNullReadException => typeof(InvalidCastException);

    [Fact]
    public Task Mistyped_column_without_a_null_surfaces_the_cast_error()
        => AssertMistypedColumnSurfacesTheCastErrorAsync();
}
