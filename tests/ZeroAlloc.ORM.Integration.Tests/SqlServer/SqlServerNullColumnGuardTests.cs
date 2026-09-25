using System.Data.Async;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.SqlServer;

// #249 — NullColumnGuardTestsBase on SQL Server 2022. SqlClient throws
// SqlNullValueException for a NULL read through GetInt32 or GetString, and its
// message names neither the column nor the ordinal.
public sealed class SqlServerNullColumnGuardTests : NullColumnGuardTestsBase
{
    private readonly SqlServerFixture _fx = new();

    protected override IAsyncDbConnection Connection => _fx.Connection;

    protected override string CreateTableSql => """
        CREATE TABLE NullGuard (
            Id INT PRIMARY KEY, Name NVARCHAR(50) NULL, Quantity INT NULL, State INT NULL,
            Ref INT NULL, Amount DECIMAL(18,2) NULL, Currency NVARCHAR(3) NULL)
        """;

    protected override ValueTask StartAsync() => _fx.InitializeAsync();

    protected override ValueTask StopAsync() => _fx.DisposeAsync();

    protected override Type ExpectedNullReadException => typeof(System.Data.SqlTypes.SqlNullValueException);

    [Fact]
    public Task Mistyped_column_without_a_null_surfaces_the_cast_error()
        => AssertMistypedColumnSurfacesTheCastErrorAsync();
}
