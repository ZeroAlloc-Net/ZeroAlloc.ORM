// AOT collision smoke for ZeroAlloc.ORM × ZeroAlloc.Telemetry. Exercises the
// composition pattern documented in docs/cookbook/observability.md:
//   - IOrderRepository declared with [Instrument] + [Trace] / [Count] / [Histogram].
//   - OrderRepository partial class implements the interface, [Query] fills bodies.
//   - At runtime the IOrderRepository surface is reached through the ZA.Telemetry-
//     generated OrderRepositoryInstrumented proxy.
//
// CI publishes this with PublishAot=true and runs the native binary. Any trimmer
// warning escalates to error via TreatWarningsAsErrors at the repo root; any
// non-zero exit code fails the workflow.

using Microsoft.Data.Sqlite;
using System.Data.Async;
using System.Data.Async.Adapters;
using ZeroAlloc.ORM.TelemetryCollision.AotSmoke;

var raw = new SqliteConnection("Data Source=:memory:");
await using (raw.ConfigureAwait(false))
{
    await raw.OpenAsync().ConfigureAwait(false);

    IAsyncDbConnection connection = raw.AsAsync();
    await using (connection.ConfigureAwait(false))
    {
        var ddl = connection.CreateCommand();
        await using (ddl.ConfigureAwait(false))
        {
            ddl.CommandText = """
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 42, 99.95);
                """;
            await ddl.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var inner = new OrderRepository(connection);

        // Reach the inner OrderRepository through the ZA.Telemetry-generated
        // proxy. If the proxy class name or shape changes upstream, this line
        // is the canary.
        IOrderRepository repo = new OrderRepositoryInstrumented(inner);

        var answer = await repo.ScalarAsync(CancellationToken.None).ConfigureAwait(false);
        if (answer != 42)
            throw new InvalidOperationException($"Expected 42, got {answer}.");

        var row = await repo.GetByIdAsync(1, CancellationToken.None).ConfigureAwait(false);
        if (row is null || row.Id != 1 || row.CustomerId != 42 || row.Total != 99.95m)
            throw new InvalidOperationException($"Unexpected row: {row}.");
    }
}

Console.WriteLine("Telemetry collision AOT smoke test passed.");
return 0;
