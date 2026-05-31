using FluentAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// v0.4 Phase A.3 — Sqlite round-trip coverage for [Command(Kind = NonQuery)].
// Four scenarios:
//   * Insert_returns_one_row_affected           — single-row INSERT.
//   * Update_returns_matching_row_count         — UPDATE matches multiple rows.
//   * Delete_returns_zero_when_no_match         — DELETE matches no rows.
//   * Touch_Task_void_completes_without_value   — Task return shape, asserts only that
//                                                  the await completes (no count to assert).
public class CommandNonQueryTests
{
    [Fact]
    public async Task Insert_returns_one_row_affected()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);

            var repo = new CommandRepo(fx.Connection);
            var rows = await repo.InsertOrderAsync(1, 42, 10.00m, CancellationToken.None).ConfigureAwait(false);

            rows.Should().Be(1);
        }
    }

    [Fact]
    public async Task Update_returns_matching_row_count()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);
            await SeedThreeOrdersAsync(fx).ConfigureAwait(false);

            var repo = new CommandRepo(fx.Connection);
            // Two of the three seeded rows share CustomerId = 42; the third has 99.
            var rows = await repo.UpdateOrdersByCustomerAsync(42, 99.99m, CancellationToken.None).ConfigureAwait(false);

            rows.Should().Be(2);
        }
    }

    [Fact]
    public async Task Delete_returns_zero_when_no_match()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);
            await SeedThreeOrdersAsync(fx).ConfigureAwait(false);

            var repo = new CommandRepo(fx.Connection);
            var rows = await repo.DeleteOrderByIdAsync(999, CancellationToken.None).ConfigureAwait(false);

            rows.Should().Be(0);
        }
    }

    [Fact]
    public async Task Touch_Task_void_completes_without_value()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);
            await SeedThreeOrdersAsync(fx).ConfigureAwait(false);

            var repo = new CommandRepo(fx.Connection);

            // Task return — no count to assert; the test passes if the await completes
            // without throwing. Implicitly exercises the arity-0 EmitCommandNonQuery
            // branch that omits the `return` statement.
            await repo.TouchOrderAsync(1, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static ValueTask SeedSchemaAsync(SqliteFixture fx) => fx.ExecuteDdlAsync(@"
        CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);");

    private static ValueTask SeedThreeOrdersAsync(SqliteFixture fx) => fx.ExecuteDdlAsync(@"
        INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 42, 10.00);
        INSERT INTO Orders (Id, CustomerId, Total) VALUES (2, 42, 20.00);
        INSERT INTO Orders (Id, CustomerId, Total) VALUES (3, 99, 30.00);");
}
