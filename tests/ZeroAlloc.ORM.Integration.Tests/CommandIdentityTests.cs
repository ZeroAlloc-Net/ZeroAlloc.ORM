using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// v0.4 Phase C.2 — Sqlite round-trip coverage for [Command(Kind = Identity)].
// Four cells cover the design matrix:
//
//   * Insert_returning_id_returns_int                    — RETURNING Id -> Task<int>.
//   * Insert_returning_id_returns_value_object           — RETURNING Id -> Task<OrderId>.
//   * Insert_via_last_insert_rowid_returns_int           — INSERT ...; SELECT
//                                                          last_insert_rowid() — the
//                                                          ;-joined fallback idiom.
//   * Insert_returning_no_rows_throws_InvalidOperationException — null-guard regression.
//
// The schema declares `Id INTEGER PRIMARY KEY` which in Sqlite is an alias for
// ROWID — values omitted from the INSERT auto-generate. The four tests share
// the SqliteFixture lifecycle pattern used by CommandScalarTests and
// CommandNonQueryTests.
public class CommandIdentityTests
{
    [Fact]
    public async Task Insert_returning_id_returns_int()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);

            var repo = new CommandRepo(fx.Connection);
            var id = await repo.InsertWithReturningAsync(42, 100.00m, CancellationToken.None).ConfigureAwait(false);

            // First insert into an empty table with INTEGER PRIMARY KEY (auto-increment alias)
            // yields rowid = 1 unless the schema specified AUTOINCREMENT (we did not).
            Assert.Equal(1, id);

            // Cross-check: a second insert produces id = 2 — verifies the
            // RETURNING clause keeps pace with the auto-increment.
            var id2 = await repo.InsertWithReturningAsync(42, 200.00m, CancellationToken.None).ConfigureAwait(false);
            Assert.Equal(2, id2);
        }
    }

    [Fact]
    public async Task Insert_returning_id_returns_value_object()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);

            var repo = new CommandRepo(fx.Connection);
            var orderId = await repo.InsertWithReturningVOAsync(42, 100.00m, CancellationToken.None).ConfigureAwait(false);

            // ConventionKind.SingleArgCtor wraps the underlying int from
            // RETURNING Id; the unwrapped Value matches the auto-generated key.
            Assert.Equal(1, orderId.Value);
        }
    }

    [Fact]
    public async Task Insert_via_last_insert_rowid_returns_int()
    {
        // Alternative provider-specific identity idiom — Sqlite's
        // `last_insert_rowid()` exposes the most recently auto-generated key
        // on the current connection. The generator's emit just passes the
        // ;-joined SQL through to ExecuteScalarAsync, so this path validates
        // that the joined-statement form survives the round-trip without
        // tripping ZAO008 (which is now exempt for [Command] methods per
        // Phase C.1).
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);

            var repo = new CommandRepo(fx.Connection);
            var id = await repo.InsertWithLastInsertRowidAsync(42, 100.00m, CancellationToken.None).ConfigureAwait(false);

            Assert.Equal(1, id);
        }
    }

    [Fact]
    public async Task Insert_returning_no_rows_throws_InvalidOperationException()
    {
        // Regression for the null-guard. A RETURNING clause that produces zero
        // rows (INSERT ... WHERE FALSE) leaves ExecuteScalarAsync returning
        // null. The generator's Identity emit must throw
        // InvalidOperationException with the "Identity command returned no
        // value" message rather than silently propagating a default 0 or
        // tripping a NullReferenceException downstream.
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);

            var repo = new CommandRepo(fx.Connection);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.InsertWithNoReturningRowAsync(42, 100.00m, CancellationToken.None))
                .ConfigureAwait(false);

            // The exception message references the Identity contract so adopters
            // know to check their SQL's RETURNING / SCOPE_IDENTITY() clause.
            Assert.Contains("Identity command", ex.Message, StringComparison.Ordinal);
        }
    }

    // v0.4 Phase C.2 — Identity-specific schema. `Id INTEGER PRIMARY KEY` is
    // the Sqlite alias for ROWID so values omitted from the INSERT auto-
    // generate; the RETURNING / last_insert_rowid clauses surface the
    // auto-generated key back to the caller. No Created column needed here —
    // the matrix focuses on the identity round-trip, not nullable columns.
    private static ValueTask SeedSchemaAsync(SqliteFixture fx) => fx.ExecuteDdlAsync(@"
        CREATE TABLE Orders (
            Id INTEGER PRIMARY KEY,
            CustomerId INTEGER NOT NULL,
            Total NUMERIC NOT NULL);");
}
