// AOT smoke consumer for ZeroAlloc.ORM. Exercises both emitted shapes
// (scalar int and FlatRow) against in-memory Sqlite. CI publishes this with
// PublishAot=true and runs the native binary — any trimmer warning escalates
// to an error via TreatWarningsAsErrors at the repo root, and a non-zero exit
// code fails the workflow.

using Microsoft.Data.Sqlite;
using System.Data.Async;
using System.Data.Async.Adapters;
using ZeroAlloc.ORM.AotSmoke;

var raw = new SqliteConnection("Data Source=:memory:");
await using (raw.ConfigureAwait(false))
{
    await raw.OpenAsync().ConfigureAwait(false);

    IAsyncDbConnection connection = raw.AsAsync();
    await using (connection.ConfigureAwait(false))
    {
        // Seed schema + one row so the FlatRow query has data to read.
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
            throw new InvalidOperationException($"Expected 42, got {answer}.");

        var row = await repo.GetByIdAsync(1, CancellationToken.None).ConfigureAwait(false);
        if (row is null || row.Id != 1 || row.CustomerId != 42 || row.Total != 99.95m)
            throw new InvalidOperationException($"Unexpected row: {row}.");
    }
}

Console.WriteLine("AOT smoke test passed.");
return 0;
