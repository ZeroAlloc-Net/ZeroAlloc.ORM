// Generator collision smoke for ZeroAlloc.ORM × ZeroAlloc.Telemetry — the
// re-attempt of the v0.6 Phase C smoke, tracked as v0.6-CLN1.
//
// Two independent source generators run in the same compilation unit:
//   - ZA.ORM's generator emits the partial-method bodies for OrderRepository.
//   - ZA.Telemetry's generator emits OrderRepositoryInstrumented, which wraps it
//     through IOrderRepository.
//
// The v0.6 attempt was backed out because ZA.Telemetry's proxy dropped the
// nullable annotation on Task<OrderRow?>, so the generated wrapper no longer
// implemented the interface (CS8613 + CS8603). ZA.Telemetry 1.4.1 fixed that.
// Because the failure was a *compile* error, simply building this project is
// most of the signal — but we also run it under AOT so trimmer and span
// behaviour are covered rather than assumed.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using System.Data.Async;
using System.Data.Async.Adapters;
using ZeroAlloc.ORM.TelemetryCollision.AotSmoke;

// --- 1. Listen to the generated ActivitySource so the proxy actually records.
// Without a listener StartActivity returns null and every SetTag short-circuits,
// which would make the tag assertions below vacuous.
var captured = new List<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = source => string.Equals(
        source.Name, "ZeroAlloc.ORM.TelemetryCollision.AotSmoke", StringComparison.Ordinal),
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = captured.Add,
};
ActivitySource.AddActivityListener(listener);

// --- 2. Exercise both emitted shapes against Sqlite, through the proxy.
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

        // The proxy is the ZA.Telemetry generator's output wrapping the ZA.ORM
        // generator's output.
        var proxy = new OrderRepositoryInstrumented(new OrderRepository(connection));

        var answer = await proxy.ScalarAsync(CancellationToken.None).ConfigureAwait(false);
        if (answer != 42)
        {
            Console.Error.WriteLine($"Telemetry collision smoke: FAIL — expected 42, got {answer}.");
            return 1;
        }

        // Routed through the interface deliberately. This is the call that
        // regressed: it only binds if the wrapper's Task<OrderRow?> signature
        // matches IOrderRepository exactly, so the dispatch is the assertion.
        var row = await FetchThroughInterface(proxy, 1, CancellationToken.None).ConfigureAwait(false);
        if (row is null || row.Id != 1 || row.CustomerId != 42 || row.Total != 99.95m)
        {
            Console.Error.WriteLine($"Telemetry collision smoke: FAIL — unexpected row: {row}.");
            return 1;
        }

        // A nullable result that is actually null must round-trip rather than throw:
        // the proxy reads the tag member off a copy with ?., so a miss is a null tag.
        var missing = await FetchThroughInterface(proxy, 404, CancellationToken.None).ConfigureAwait(false);
        if (missing is not null)
        {
            Console.Error.WriteLine($"Telemetry collision smoke: FAIL — expected null for id 404, got {missing}.");
            return 1;
        }
    }
}

// --- 3. Verify the spans and tags the proxy was supposed to record.
if (captured.Count != 3)
{
    Console.Error.WriteLine($"Telemetry collision smoke: FAIL — expected 3 spans, got {captured.Count}.");
    return 1;
}

var hit = captured.Find(a => string.Equals(a.OperationName, "orders.get_by_id", StringComparison.Ordinal)
                          && a.GetTagItem("orders.total") is not null);
if (hit is null)
{
    Console.Error.WriteLine("Telemetry collision smoke: FAIL — no span carried the orders.total result tag.");
    return 1;
}

if (hit.GetTagItem("orders.id") is not int)
{
    Console.Error.WriteLine("Telemetry collision smoke: FAIL — orders.id argument tag missing.");
    return 1;
}

Console.WriteLine("Telemetry collision smoke: PASS — ZA.ORM × ZA.Telemetry coexist under AOT, "
                + "nullable annotations preserved and span tags recorded.");
return 0;

// Takes the interface, not the concrete proxy: the parameter type is what forces
// the compiler to check that OrderRepositoryInstrumented implements
// IOrderRepository.GetByIdAsync with a matching Task<OrderRow?> signature. Under
// the pre-1.4.1 generator this failed to compile with CS8613.
static Task<OrderRow?> FetchThroughInterface(IOrderRepository repository, int id, CancellationToken ct)
    => repository.GetByIdAsync(id, ct);
