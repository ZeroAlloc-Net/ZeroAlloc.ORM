using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// v0.4 Phase B.2 — Sqlite round-trip coverage for [Command(Kind = Scalar)].
// Mirrors the snapshot matrix:
//   * Count_returns_row_count                    — Task<int> from COUNT(*).
//   * Sum_returns_aggregate_decimal              — Task<decimal> from SUM(Total).
//   * MaxCreated_on_empty_table_returns_null     — Task<DateTime?> on empty table.
//   * Sum_returns_value_object                   — Task<TotalAmount> (VO) over SUM.
//
// Schema includes a Created (TEXT) column so the MAX(Created) variant has a
// nullable DateTime target. Sqlite stores DateTime as ISO-8601 text by default
// when Microsoft.Data.Sqlite is the driver; the generator's GetDateTime path
// reads it back through that adapter.
public class CommandScalarTests
{
    [Fact]
    public async Task Count_returns_row_count()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);
            await SeedThreeOrdersAsync(fx).ConfigureAwait(false);

            var repo = new CommandRepo(fx.Connection);
            var count = await repo.CountOrdersAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.Equal(3, count);
        }
    }

    [Fact]
    public async Task Sum_returns_aggregate_decimal()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);
            await SeedThreeOrdersAsync(fx).ConfigureAwait(false);

            var repo = new CommandRepo(fx.Connection);
            // Two of the three seeded rows share CustomerId = 42 with totals 10 + 20.
            var sum = await repo.SumTotalsForCustomerAsync(42, CancellationToken.None).ConfigureAwait(false);

            // Direct Xunit Assert.Equal (not FluentAssertions Should) keeps the
            // assertion EPS06-clean on the decimal struct, matching the pattern in
            // CommandNonQueryTests.
            Assert.Equal(30.00m, sum);
        }
    }

    [Fact]
    public async Task MaxCreated_on_empty_table_returns_null()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);
            // Intentionally no row-seed; MAX over zero rows returns NULL which the
            // generator's DBNull guard converts to a C# null.

            var repo = new CommandRepo(fx.Connection);
            var max = await repo.MaxCreatedAsync(CancellationToken.None).ConfigureAwait(false);

            // Direct Assert.Null instead of FluentAssertions Should().BeNull() —
            // the latter trips EPS06 (hidden Nullable<DateTime> copy via property
            // expression). Mirrors the EPS06-safe assertion style used elsewhere
            // in the test surface for nullable value types.
            Assert.Null(max);
        }
    }

    [Fact]
    public async Task Sum_returns_value_object()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);
            await SeedThreeOrdersAsync(fx).ConfigureAwait(false);

            var repo = new CommandRepo(fx.Connection);
            var total = await repo.SumTotalsValueObjectAsync(42, CancellationToken.None).ConfigureAwait(false);

            // ConventionKind.SingleArgCtor unwrap: the VO's Value property carries
            // the underlying decimal that round-tripped through ExecuteScalarAsync.
            Assert.Equal(30.00m, total.Value);
        }
    }

    [Fact]
    public async Task NonNullable_scalar_on_empty_result_throws_InvalidOperationException()
    {
        // v0.4 Phase B code-review Fix 1 regression. A non-nullable Task<decimal>
        // scalar pointed at a SELECT that produces NO ROWS yields a null
        // `__result` from ExecuteScalarAsync. Without the generator's explicit
        // null-guard, Convert.ToDecimal(null, ic) silently returns 0 — a
        // data-corruption hazard. The guard must throw InvalidOperationException
        // so callers see the missing scalar instead of a sentinel zero.
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);
            // Intentionally NO row seeded — the WHERE clause matches zero rows.

            var repo = new CommandRepo(fx.Connection);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.GetTotalForMissingIdAsync(CancellationToken.None))
                .ConfigureAwait(false);
        }
    }

    private static ValueTask SeedSchemaAsync(SqliteFixture fx) => fx.ExecuteDdlAsync(@"
        CREATE TABLE Orders (
            Id INTEGER PRIMARY KEY,
            CustomerId INTEGER NOT NULL,
            Total NUMERIC NOT NULL,
            Created TEXT NULL);");

    private static ValueTask SeedThreeOrdersAsync(SqliteFixture fx) => fx.ExecuteDdlAsync(@"
        INSERT INTO Orders (Id, CustomerId, Total, Created) VALUES (1, 42, 10.00, '2026-05-01T10:00:00');
        INSERT INTO Orders (Id, CustomerId, Total, Created) VALUES (2, 42, 20.00, '2026-05-02T10:00:00');
        INSERT INTO Orders (Id, CustomerId, Total, Created) VALUES (3, 99, 30.00, '2026-05-03T10:00:00');");
}
