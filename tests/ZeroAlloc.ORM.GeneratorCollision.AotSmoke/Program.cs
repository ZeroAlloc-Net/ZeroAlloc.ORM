// Generator collision smoke for ZeroAlloc.ORM × ZeroAlloc.Rest — the v1.0
// release gate (see docs/plans/2026-06-01-v0.7-implementation.md, Phase B).
//
// Two independent source generators run in the same compilation unit:
//   - ZA.ORM's generator emits the partial-method body for SmokeRepo.
//   - ZA.Rest's generator emits OrderApiClient (implements IOrderApi).
// This program forces both emitted shapes to live, then CI publishes it with
// PublishAot=true and runs the native binary. Any partial-slot collision,
// assembly-attribute conflict, or trimmer warning lights up here.

using System;
using Microsoft.Data.Sqlite;
using System.Data.Async;
using System.Data.Async.Adapters;
using ZeroAlloc.ORM.GeneratorCollision.AotSmoke;

// --- 1. ZA.Rest generator output: prove the emitted client type exists.
// We don't fire HTTP — the ILC analysis of the emitted proxy is the signal.
var restClientType = Type.GetType(
    "ZeroAlloc.ORM.GeneratorCollision.AotSmoke.OrderApiClient");
if (restClientType is null)
{
    Console.Error.WriteLine(
        "Collision smoke: FAIL — ZA.Rest generator did not emit OrderApiClient.");
    return 1;
}

if (!typeof(IOrderApi).IsAssignableFrom(restClientType))
{
    Console.Error.WriteLine(
        "Collision smoke: FAIL — OrderApiClient should implement IOrderApi.");
    return 1;
}

// --- 2. ZA.ORM generator output: exercise both emitted shapes against Sqlite.
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

        var repo = new SmokeRepo(connection);

        var answer = await repo.ScalarAsync(CancellationToken.None).ConfigureAwait(false);
        if (answer != 42)
        {
            Console.Error.WriteLine($"Collision smoke: FAIL — expected 42, got {answer}.");
            return 1;
        }

        var row = await repo.GetByIdAsync(1, CancellationToken.None).ConfigureAwait(false);
        if (row is null || row.Id != 1 || row.CustomerId != 42 || row.Total != 99.95m)
        {
            Console.Error.WriteLine($"Collision smoke: FAIL — unexpected row: {row}.");
            return 1;
        }
    }
}

Console.WriteLine("Collision smoke: PASS — ZA.ORM × ZA.Rest coexist under AOT.");
return 0;
