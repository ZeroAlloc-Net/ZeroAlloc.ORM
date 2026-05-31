using System.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using System.Data.Async;
using System.Data.Async.Adapters;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// v0.3 Phase C.3 — round-trip integration coverage for the streaming emit.
// Three scenarios:
//   * Streams_seeded_rows_in_order — async-foreach consumes all rows and
//     observes the seeded order + scalar values.
//   * Early_break_cleans_up_reader — breaking out of the loop after the
//     first row still closes the connection that the repo opened, proving
//     the iterator's finally hook runs through IAsyncEnumerator.DisposeAsync.
//   * Cancellation_via_token_stops_streaming — cancelling the token mid-
//     stream surfaces an OperationCanceledException.
//
// TODO(v0.3 backlog): lift the keeper-connection / shared-cache in-memory pattern into SqliteFixture
// as SqliteFixture.CreateSharedMemory(string name) once a second streaming-style early-close test lands.
public class StreamingTests
{
    [Fact]
    public async Task Streams_seeded_rows_in_order()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 42, 10.00);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (2, 42, 20.00);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (3, 99, 30.00);").ConfigureAwait(false);

            var repo = new StreamingRepo(fx.Connection);
            var rows = new List<OrderRow>();
            await foreach (var row in repo.StreamAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                rows.Add(row);
            }

            rows.Should().HaveCount(3);
            rows[0].Should().Be(new OrderRow(1, 42, 10.00m));
            rows[1].Should().Be(new OrderRow(2, 42, 20.00m));
            rows[2].Should().Be(new OrderRow(3, 99, 30.00m));
        }
    }

    [Fact]
    public async Task Early_break_cleans_up_reader_and_closes_connection()
    {
        // Use a shared-cache in-memory database so closing the connection does
        // NOT wipe the seeded schema/rows. A unique database name keeps tests
        // isolated even with shared cache.
        //   * `:memory:` databases are per-connection — closing wipes the data.
        //   * `Mode=Memory;Cache=Shared` makes the database name-scoped so a
        //     second open on the same name sees the same data.
        var dbName = $"streaming-early-break-{Guid.NewGuid():N}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        // Keeper holds the database alive for the duration of the test — the
        // shared-cache DB is destroyed when the last connection closes. The keeper
        // stays as a raw SqliteConnection because its lifetime is what holds the
        // shared-cache DB alive; we only route the seed through .AsAsync() so the
        // seeding ADO surface matches the rest of the integration suite.
        var keeper = new SqliteConnection(connectionString);
        await using (keeper.ConfigureAwait(false))
        {
            await keeper.OpenAsync().ConfigureAwait(false);

            // Seed via the keeper, going through the async wrapper for consistency
            // with the rest of the integration suite (SqliteFixture also uses
            // .AsAsync() for command execution).
            IAsyncDbConnection keeperAsync = keeper.AsAsync();
            var seedCmd = keeperAsync.CreateCommand();
            await using (seedCmd.ConfigureAwait(false))
            {
                seedCmd.CommandText = @"
                    CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);
                    INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 42, 10.00);
                    INSERT INTO Orders (Id, CustomerId, Total) VALUES (2, 42, 20.00);
                    INSERT INTO Orders (Id, CustomerId, Total) VALUES (3, 99, 30.00);";
                await seedCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            // Repo connection starts closed so the streaming emit's
            // __openedHere branch fires and owns the open/close pairing.
            var raw = new SqliteConnection(connectionString);
            await using (raw.ConfigureAwait(false))
            {
                raw.State.Should().Be(ConnectionState.Closed);
                IAsyncDbConnection async = raw.AsAsync();
                var repo = new StreamingRepo(async);

                await foreach (var row in repo.StreamAllAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    // Break after the first row — the iterator's finally must
                    // still run via IAsyncEnumerator.DisposeAsync at the end of
                    // the surrounding await-foreach.
                    row.Id.Should().Be(1);
                    break;
                }

                // The repo opened the connection itself, so it must close it
                // again even though the consumer exited early.
                raw.State.Should().Be(ConnectionState.Closed);
            }
        }
    }

    [Fact]
    public async Task Cancellation_via_token_stops_streaming()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 42, 10.00);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (2, 42, 20.00);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (3, 99, 30.00);").ConfigureAwait(false);

            var repo = new StreamingRepo(fx.Connection);
            using var cts = new CancellationTokenSource();

            Func<Task> act = async () =>
            {
                await foreach (var row in repo.StreamAllAsync(cts.Token).ConfigureAwait(false))
                {
                    // Cancel after the first row — subsequent ReadAsync calls observe
                    // the cancellation and surface as OperationCanceledException.
                    cts.Cancel();
                }
            };

            // Tighten the assertion: verify the OCE carries a cancelled token AND
            // that our CTS is the one we cancelled — guards against an ambient or
            // unrelated cancellation passing the test. Reference equality with
            // cts.Token is too strict in practice: Sqlite's ReadAsync chains a
            // linked-token internally, so the OCE surfaces the linked token rather
            // than the original cts.Token. Asserting both `IsCancellationRequested`
            // is the strongest provenance check available without coupling to
            // provider-internal token plumbing.
            var thrown = await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
            thrown.Which.CancellationToken.IsCancellationRequested.Should().BeTrue();
            cts.IsCancellationRequested.Should().BeTrue();
        }
    }
}
