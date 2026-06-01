using System.Data;
using FluentAssertions;
using Npgsql;
using System.Data.Async;
using System.Data.Async.Adapters;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// v0.6 Phase A.2 — Postgres-backed mirror of the Sqlite StreamingTests.
// Three scenarios:
//   * Streams_seeded_rows_in_order — async-foreach consumes all rows.
//   * Early_break_cleans_up_reader_and_closes_connection — breaking out
//     of the loop still closes a repo-opened connection via the iterator's
//     finally. Uses a second NpgsqlConnection pointing at the same fixture
//     container to verify open/close pairing.
//   * Cancellation_via_token_stops_streaming — cancelling the token mid-
//     stream surfaces an OperationCanceledException.
[Trait("Provider", "Postgres")]
public sealed class StreamingTests
{
    [Fact]
    public async Task Streams_seeded_rows_in_order()
    {
        var fx = new PostgresFixture();
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
        // Unlike Sqlite's in-memory-shared-cache dance, Postgres just shares a
        // server-side database — we can spin up TWO NpgsqlConnections pointing
        // at the same container and the seed visible to the keeper stays
        // visible to the probe connection. The keeper seeds + keeps the
        // schema alive; the probe connection starts closed so the streaming
        // emit's `__openedHere` branch fires and owns the open/close pairing.
        var fx = new PostgresFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            await fx.ExecuteDdlAsync(@"
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 42, 10.00);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (2, 42, 20.00);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (3, 99, 30.00);").ConfigureAwait(false);

            var raw = new NpgsqlConnection(fx.ConnectionString);
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
    public async Task Cancellation_via_pre_cancelled_token_stops_streaming()
    {
        // Provider-difference note: mid-stream cancellation on Sqlite
        // surfaces an OCE because Microsoft.Data.Sqlite doesn't prefetch —
        // every ReadAsync hits the cancellation check. Npgsql buffers
        // result-set rows in a network read buffer, so a small seed plus
        // cancel-after-first-row often races: the buffered rows complete
        // synchronously without re-entering the cancel-aware await path,
        // and the stream exits naturally before a network round-trip can
        // observe the token.
        //
        // The provider-agnostic invariant that matters is that the
        // streaming iterator *honours* a cancelled token. Pre-cancelling
        // BEFORE the first MoveNextAsync forces ReadAsync to enter with
        // a cancelled token, which surfaces the OCE deterministically on
        // both Sqlite and Postgres. The "cancel mid-stream against a tiny
        // result-set" assertion belongs to the Sqlite suite — it stays
        // there for behavioural-coverage continuity.
        var fx = new PostgresFixture();
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
            cts.Cancel();

            Func<Task> act = async () =>
            {
                await foreach (var row in repo.StreamAllAsync(cts.Token).ConfigureAwait(false))
                {
                    // Unreachable in normal flow — the OCE fires on the
                    // first MoveNextAsync because the token is already
                    // cancelled when ReadAsync (or its enclosing OpenAsync/
                    // ExecuteReaderAsync) examines it.
                }
            };

            var thrown = await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
            thrown.Which.CancellationToken.IsCancellationRequested.Should().BeTrue();
            cts.IsCancellationRequested.Should().BeTrue();
        }
    }
}
