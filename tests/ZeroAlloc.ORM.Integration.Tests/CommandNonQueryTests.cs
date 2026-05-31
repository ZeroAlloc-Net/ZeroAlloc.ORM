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

            // Task return — no count to surface; the test exercises the arity-0
            // EmitCommandNonQuery branch (no `return` statement in the emit body).
            // We still need to prove the body actually ran the UPDATE — a regression
            // that emits a no-op body would silently pass otherwise — so we follow
            // up with a direct SELECT against the touched row and assert the
            // Total column reflects the UPDATE's side effect.
            await repo.TouchOrderAsync(1, CancellationToken.None).ConfigureAwait(false);

            var probe = fx.Connection.CreateCommand();
            await using (probe.ConfigureAwait(false))
            {
                probe.CommandText = "SELECT Total FROM Orders WHERE Id = 1";
                var result = await probe.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
                // Seeded value was 10.00; the [Command] UPDATE adds 1 to it.
                // Direct Xunit Assert.Equal (not FluentAssertions Should) keeps the
                // assertion EPS06-clean on the decimal struct.
                Assert.Equal(11.00m, Convert.ToDecimal(result, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }

    private static ValueTask SeedSchemaAsync(SqliteFixture fx) => fx.ExecuteDdlAsync(@"
        CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);");

    private static ValueTask SeedThreeOrdersAsync(SqliteFixture fx) => fx.ExecuteDdlAsync(@"
        INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 42, 10.00);
        INSERT INTO Orders (Id, CustomerId, Total) VALUES (2, 42, 20.00);
        INSERT INTO Orders (Id, CustomerId, Total) VALUES (3, 99, 30.00);");
}
