using FluentAssertions;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Data.Async;
using System.Data.Async.Adapters;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

public class LifecycleTests
{
    [Fact]
    public async Task Closed_connection_opens_queries_then_closes_back()
    {
        // Deliberately do NOT keep the connection open — the generator's emit must
        // detect __openedHere=true and run the open/close round-trip itself.
        var raw = new SqliteConnection("Data Source=:memory:");
        await using (raw.ConfigureAwait(false))
        {
            await raw.OpenAsync().ConfigureAwait(false);
            // ^^^ Sqlite requires the underlying connection to be open at least
            // once for the in-memory database to exist; close after to set up
            // the closed-state test scenario.
            await raw.CloseAsync().ConfigureAwait(false);
            raw.State.Should().Be(ConnectionState.Closed);

            IAsyncDbConnection async = raw.AsAsync();

            var repo = new LifecycleRepo(async);
            var first = await repo.AnswerAsync(CancellationToken.None).ConfigureAwait(false);
            first.Should().Be(42);

            // Connection must be closed again after the call returned.
            raw.State.Should().Be(ConnectionState.Closed);

            // Second invocation also works (proves close->open->close->open->close round-trips cleanly).
            var second = await repo.AnswerAsync(CancellationToken.None).ConfigureAwait(false);
            second.Should().Be(42);

            raw.State.Should().Be(ConnectionState.Closed);
        }
    }
}
