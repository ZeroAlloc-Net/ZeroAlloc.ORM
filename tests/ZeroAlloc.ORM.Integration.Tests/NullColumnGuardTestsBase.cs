using System.Data.Async;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// #249 — a NULL read into a non-nullable member throws
// ZeroAllocOrmMaterializationException naming the column, the parameter it
// binds to and the type. Before, the provider threw its own exception: SqlClient
// a SqlNullValueException that names nothing, Sqlite an InvalidOperationException
// that names the ordinal and Npgsql an InvalidCastException that names the
// column. None named the member it was read into.
//
// One subclass per provider supplies the connection and the table's DDL; the
// seed and every case below run unchanged on Sqlite, SQL Server and Postgres.
public abstract class NullColumnGuardTestsBase : IAsyncLifetime
{
    // Rows 1 to 5 each hold exactly one NULL, row 6 is NULL in every column
    // but Id and row 7 has no NULL.
    private const string SeedSql = """
        INSERT INTO NullGuard (Id, Name, Quantity, State, Ref, Amount, Currency) VALUES
            (1, NULL, 1, 0, 1, 1.5, 'EUR'),
            (2, 'n', NULL, 0, 1, 1.5, 'EUR'),
            (3, 'n', 1, NULL, 1, 1.5, 'EUR'),
            (4, 'n', 1, 0, NULL, 1.5, 'EUR'),
            (5, 'n', 1, 0, 1, 1.5, NULL),
            (6, NULL, NULL, NULL, NULL, NULL, NULL),
            (7, 'seven', 2, 1, 3, 4.5, 'USD')
        """;

    private const string RowType = "ZeroAlloc.ORM.Integration.Tests.NullGuardRow";

    protected abstract IAsyncDbConnection Connection { get; }

    // CREATE TABLE NullGuard with the nullable columns Id, Name, Quantity,
    // State, Ref, Amount and Currency, in the provider's own column types.
    protected abstract string CreateTableSql { get; }

    protected abstract ValueTask StartAsync();

    protected abstract ValueTask StopAsync();

    // The exception the provider throws for a NULL read through a typed getter,
    // which the ORM exception carries as InnerException.
    protected abstract Type ExpectedNullReadException { get; }

    // A genuine cast error, text read as int, still surfaces as the provider's
    // own InvalidCastException. Called by the SQL Server and Postgres subclasses;
    // Sqlite converts text to an integer instead of throwing.
    protected async Task AssertMistypedColumnSurfacesTheCastErrorAsync()
    {
        var repo = new NullGuardRepo(Connection);

        // ThrowsAsync matches the exact type, so a ZeroAllocOrmMaterializationException
        // here would fail the test.
        await Assert.ThrowsAsync<InvalidCastException>(
            () => repo.GetMistypedRowAsync(7, CancellationToken.None)).ConfigureAwait(false);
    }

    public async ValueTask InitializeAsync()
    {
        await StartAsync().ConfigureAwait(false);
        await ExecuteAsync(CreateTableSql).ConfigureAwait(false);
        await ExecuteAsync(SeedSql).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Null_into_string_throws_naming_the_column()
    {
        var repo = new NullGuardRepo(Connection);

        await AssertNullColumnThrowsAsync(
            () => repo.GetRowAsync(1, CancellationToken.None), "Name", "Name", RowType, "string").ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_into_int_throws_naming_the_column()
    {
        var repo = new NullGuardRepo(Connection);

        await AssertNullColumnThrowsAsync(
            () => repo.GetRowAsync(2, CancellationToken.None), "Quantity", "Quantity", RowType, "int").ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_into_enum_throws_naming_the_column()
    {
        var repo = new NullGuardRepo(Connection);

        await AssertNullColumnThrowsAsync(
            () => repo.GetRowAsync(3, CancellationToken.None), "State", "State", RowType,
            "ZeroAlloc.ORM.Integration.Tests.Status").ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_into_value_object_throws_naming_the_column()
    {
        var repo = new NullGuardRepo(Connection);

        await AssertNullColumnThrowsAsync(
            () => repo.GetRowAsync(4, CancellationToken.None), "Ref", "Ref", RowType,
            "ZeroAlloc.ORM.Integration.Tests.OrderId").ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_into_record_constructor_parameter_throws_naming_the_column()
    {
        var repo = new NullGuardRepo(Connection);

        await AssertNullColumnThrowsAsync(
            () => repo.GetMoneyRowAsync(5, CancellationToken.None), "Currency", "Currency",
            "ZeroAlloc.ORM.Integration.Tests.Money", "string").ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_into_class_constructor_parameter_throws_naming_the_column()
    {
        var repo = new NullGuardRepo(Connection);

        await AssertNullColumnThrowsAsync(
            () => repo.GetEntityAsync(2, CancellationToken.None), "Quantity", "quantity",
            "ZeroAlloc.ORM.Integration.Tests.NullGuardEntity", "int").ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_in_a_list_row_throws_naming_the_column()
    {
        var repo = new NullGuardRepo(Connection);

        await AssertNullColumnThrowsAsync(
            () => repo.ListRowsAsync(1, CancellationToken.None), "Name", "Name", RowType, "string").ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_in_a_streamed_row_throws_naming_the_column()
    {
        var repo = new NullGuardRepo(Connection);

        await AssertNullColumnThrowsAsync(
            async () =>
            {
                await foreach (var row in repo.StreamRowsAsync(3, CancellationToken.None).ConfigureAwait(false))
                {
                    Assert.Fail($"Expected a throw, read {row}.");
                }
            },
            "State", "State", RowType, "ZeroAlloc.ORM.Integration.Tests.Status").ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_in_a_multi_result_row_element_throws_naming_the_column()
    {
        var repo = new NullGuardRepo(Connection);

        await AssertNullColumnThrowsAsync(
            () => repo.GetHeadAndRowsAsync(2, 7, CancellationToken.None), "Quantity", "Quantity", RowType, "int").ConfigureAwait(false);
    }

    [Fact]
    public async Task Null_in_a_multi_result_list_element_throws_naming_the_column()
    {
        var repo = new NullGuardRepo(Connection);

        await AssertNullColumnThrowsAsync(
            () => repo.GetHeadAndRowsAsync(7, 4, CancellationToken.None), "Ref", "Ref", RowType,
            "ZeroAlloc.ORM.Integration.Tests.OrderId").ConfigureAwait(false);
    }

    [Fact]
    public async Task Multi_result_without_null_reads_both_elements()
    {
        var repo = new NullGuardRepo(Connection);

        var (head, rows) = await repo.GetHeadAndRowsAsync(7, 7, CancellationToken.None).ConfigureAwait(false);

        var expected = new NullGuardRow(7, "seven", 2, Status.Cancelled, new OrderId(3));
        Assert.Equal(expected, head);
        Assert.Equal([expected], rows);
    }

    [Fact]
    public async Task Null_into_nullable_members_reads_back_as_null()
    {
        var repo = new NullGuardRepo(Connection);

        var row = await repo.GetNullableRowAsync(6, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(new NullableGuardRow(6, null, null, null, null), row);
    }

    [Fact]
    public async Task Values_read_into_nullable_members()
    {
        var repo = new NullGuardRepo(Connection);

        var row = await repo.GetNullableRowAsync(7, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(new NullableGuardRow(7, "seven", 2, Status.Cancelled, new OrderId(3)), row);
    }

    [Fact]
    public async Task Values_read_into_non_nullable_members()
    {
        var repo = new NullGuardRepo(Connection);

        var row = await repo.GetRowAsync(7, CancellationToken.None).ConfigureAwait(false);
        var money = await repo.GetMoneyRowAsync(7, CancellationToken.None).ConfigureAwait(false);
        var entity = await repo.GetEntityAsync(7, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(new NullGuardRow(7, "seven", 2, Status.Cancelled, new OrderId(3)), row);
        Assert.Equal(new NullGuardMoneyRow(7, new Money(4.5m, "USD")), money);
        Assert.NotNull(entity);
        Assert.Equal("seven", entity.Name);
        Assert.Equal(2, entity.Quantity);
    }

    // #249 — the catch only replaces an exception when a non-nullable column is
    // NULL. Row 7 holds Quantity 2, which CheckedQuantity rejects with
    // InvalidOperationException, a type the filter inspects; with no NULL column
    // it must come through as it was thrown.
    [Fact]
    public async Task Rejected_value_without_a_null_surfaces_the_original_exception()
    {
        var repo = new NullGuardRepo(Connection);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.GetCheckedAsync(7, CancellationToken.None)).ConfigureAwait(false);

        Assert.Equal("Quantity 2 is below the minimum of 5.", ex.Message);
    }

    private async ValueTask ExecuteAsync(string sql)
    {
        var cmd = Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    // The column name is compared ignoring case: Postgres folds the unquoted
    // identifiers in the SELECT list to lower case, so a positional read names
    // `name` where the other providers name `Name`.
    private async Task AssertNullColumnThrowsAsync(
        Func<Task> call, string column, string parameter, string owner, string type)
    {
        var ex = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(call).ConfigureAwait(false);
        // The provider's own exception is kept, so its details are not lost.
        Assert.NotNull(ex.InnerException);
        Assert.IsType(ExpectedNullReadException, ex.InnerException, exactMatch: true);
        Assert.Contains($"Column '{column}' is NULL", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"parameter '{parameter}' of '{owner}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"non-nullable type '{type}'", ex.Message, StringComparison.Ordinal);
    }
}
