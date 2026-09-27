// AOT smoke consumer for ZeroAlloc.ORM. Exercises both emitted shapes
// (scalar int and FlatRow) against in-memory Sqlite, plus DateOnly and TimeOnly
// parameters, columns and scalars (#256). CI publishes this with
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
                CREATE TABLE Shifts (Id INTEGER PRIMARY KEY, Day TEXT NOT NULL, Start TEXT NOT NULL, EndDay TEXT NULL);
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

        var day = new DateOnly(2024, 1, 2);
        var start = new TimeOnly(13, 14, 15);
        await repo.InsertShiftAsync(1, day, start, null, CancellationToken.None).ConfigureAwait(false);
        var shift = await repo.GetShiftAsync(1, CancellationToken.None).ConfigureAwait(false);
        if (shift != new ShiftRow(1, day, start, null))
            throw new InvalidOperationException($"Unexpected shift: {shift}.");
        var shiftStart = await repo.ShiftStartAsync(1, CancellationToken.None).ConfigureAwait(false);
        if (shiftStart != start)
            throw new InvalidOperationException($"Expected {start}, got {shiftStart}.");
    }
}

Console.WriteLine("AOT smoke test passed.");
return 0;
