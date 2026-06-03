using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// v1.5 Task 7 — Sqlite end-to-end coverage for the IAsyncDbTransaction
// parameter feature. The Generator snapshot tests (Emit/TransactionParameterTests)
// pin the EMIT SHAPE (`__cmd.Transaction = @tx;`); these two tests prove
// the SHAPE participates in the active transaction at runtime:
//
//   * Two_inserts_share_transaction_and_commit_atomically — both rows visible
//     after CommitAsync.
//   * Two_inserts_share_transaction_and_rollback_when_not_committed — neither
//     row visible after DisposeAsync-without-commit (implicit rollback).
//
// Sqlite-only is sufficient: the emit is provider-agnostic
// (`cmd.Transaction = tx` is the standard ADO.NET contract every provider
// honors), so Postgres + SqlClient are covered structurally by the snapshot
// tests.
public class TransactionParameterTests
{
    [Fact]
    public async Task Two_inserts_share_transaction_and_commit_atomically()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);

            var repo = new TransactionParameterRepo(fx.Connection);
            var tx = await fx.Connection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                await repo.InsertAsync("A", tx, CancellationToken.None).ConfigureAwait(false);
                await repo.InsertAsync("B", tx, CancellationToken.None).ConfigureAwait(false);
                await tx.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            var count = await repo.CountAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.Equal(2, count);
        }
    }

    [Fact]
    public async Task Two_inserts_share_transaction_and_rollback_when_not_committed()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await SeedSchemaAsync(fx).ConfigureAwait(false);

            var repo = new TransactionParameterRepo(fx.Connection);
            var tx = await fx.Connection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                await repo.InsertAsync("A", tx, CancellationToken.None).ConfigureAwait(false);
                await repo.InsertAsync("B", tx, CancellationToken.None).ConfigureAwait(false);
                // No CommitAsync — DisposeAsync rolls back the open transaction.
            }

            var count = await repo.CountAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.Equal(0, count);
        }
    }

    private static ValueTask SeedSchemaAsync(SqliteFixture fx) => fx.ExecuteDdlAsync(@"
        CREATE TABLE Things (
            Id INTEGER PRIMARY KEY,
            Name TEXT NOT NULL);");
}
