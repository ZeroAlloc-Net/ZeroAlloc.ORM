// AOT smoke consumer for ZeroAlloc.ORM. Exercises both emitted shapes
// (scalar int and FlatRow) against in-memory Sqlite, plus DateOnly and TimeOnly
// parameters, columns and scalars (#256), and value-type shapes: GetFieldValue<T>
// columns, enums in int and string storage, nullable primitives and user structs. CI publishes this with
// PublishAot=true and runs the native binary — any trimmer warning escalates
// to an error via TreatWarningsAsErrors at the repo root, and a non-zero exit
// code fails the workflow.

using Microsoft.Data.Sqlite;
using System.Data.Async;
using System.Data.Async.Adapters;
using System.Globalization;
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
                CREATE TABLE ValueRows (
                    Id INTEGER PRIMARY KEY, At TEXT NOT NULL, Duration TEXT NOT NULL, Token BLOB NOT NULL,
                    Payload BLOB NOT NULL, Priority INTEGER NOT NULL, Status TEXT NOT NULL, Score INTEGER NULL,
                    Ref BLOB NULL, Sku INTEGER NOT NULL, AltSku INTEGER NULL);
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

        // Value-type shapes, ZeroAlloc-Net/ZeroAlloc.Cache#182 and dotnet/runtime#134799: a generic
        // path that only ever saw strings shipped a NativeAOT hang on Nullable<T>. Every field of
        // every row is compared, so a wrong value fails as loudly as an exception.
        var first = new ValueRow(
            1, new DateTimeOffset(2024, 3, 4, 5, 6, 7, 890, TimeSpan.FromHours(2)), new TimeSpan(1, 2, 3, 4, 567),
            new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"), [1, 2, 3, 250], Priority.Normal, Status.Active,
            7, null, new Sku(5), new Sku(9));
        var second = new ValueRow(
            2, new DateTimeOffset(2025, 12, 31, 23, 59, 58, TimeSpan.FromHours(-5)), TimeSpan.FromMinutes(90),
            new Guid("7c9e6679-7425-40de-944b-e07fc1f90ae7"), [0], Priority.High, Status.Archived,
            null, new Guid("a8098c1a-f86e-11da-bd1a-00112444be1e"), new Sku(6), null);
        foreach (var v in (ValueRow[])[first, second])
        {
            var inserted = await repo.InsertValueRowAsync(
                v.Id, v.At, v.Duration, v.Token, v.Payload, v.Priority, v.Status, v.Score, v.Ref, v.Sku, v.AltSku,
                CancellationToken.None).ConfigureAwait(false);
            if (inserted != 1)
                throw new InvalidOperationException($"Expected 1 inserted row for {v.Id}, got {inserted}.");
        }

        AssertValueRow(await repo.GetValueRowAsync(1, CancellationToken.None).ConfigureAwait(false), first, "single 1");
        AssertValueRow(await repo.GetValueRowAsync(2, CancellationToken.None).ConfigureAwait(false), second, "single 2");

        var high = await repo.ListByPriorityAsync(Priority.High, CancellationToken.None).ConfigureAwait(false);
        if (high.Count != 1)
            throw new InvalidOperationException($"Expected 1 High row, got {high.Count}.");
        AssertValueRow(high[0], second, "list by int enum");

        var streamed = new List<ValueRow>();
        await foreach (var v in repo.StreamByStatusAsync(Status.Active, CancellationToken.None).ConfigureAwait(false))
            streamed.Add(v);
        if (streamed.Count != 1)
            throw new InvalidOperationException($"Expected 1 Active row, got {streamed.Count}.");
        AssertValueRow(streamed[0], first, "stream by string enum");

        var score = await repo.ScoreAsync(1, CancellationToken.None).ConfigureAwait(false);
        if (score != 7)
            throw new InvalidOperationException($"Expected score 7, got {score?.ToString(CultureInfo.InvariantCulture) ?? "null"}.");
        var noScore = await repo.ScoreAsync(2, CancellationToken.None).ConfigureAwait(false);
        if (noScore is not null)
            throw new InvalidOperationException($"Expected null score, got {noScore.Value.ToString(CultureInfo.InvariantCulture)}.");
        var noRowScore = await repo.ScoreAsync(99, CancellationToken.None).ConfigureAwait(false);
        if (noRowScore is not null)
            throw new InvalidOperationException($"Expected null score for a missing row, got {noRowScore.Value.ToString(CultureInfo.InvariantCulture)}.");

        var noRef = await repo.RefAsync(1, CancellationToken.None).ConfigureAwait(false);
        if (noRef is not null)
            throw new InvalidOperationException($"Expected null ref, got {noRef}.");
        var someRef = await repo.RefAsync(2, CancellationToken.None).ConfigureAwait(false);
        if (someRef != second.Ref)
            throw new InvalidOperationException($"Expected ref {second.Ref}, got {someRef?.ToString() ?? "null"}.");

        var priority = await repo.PriorityAsync(2, CancellationToken.None).ConfigureAwait(false);
        if (priority != Priority.High)
            throw new InvalidOperationException($"Expected {Priority.High}, got {priority}.");
        var status = await repo.StatusAsync(1, CancellationToken.None).ConfigureAwait(false);
        if (status != Status.Active)
            throw new InvalidOperationException($"Expected {Status.Active}, got {status}.");
        var sku = await repo.SkuAsync(1, CancellationToken.None).ConfigureAwait(false);
        if (sku != new Sku(5))
            throw new InvalidOperationException($"Expected Sku 5, got {sku}.");

        Console.WriteLine("Value-type shapes passed.");
    }
}

Console.WriteLine("AOT smoke test passed.");
return 0;

static void AssertValueRow(ValueRow? actual, ValueRow expected, string label)
{
    if (actual is null)
        throw new InvalidOperationException($"{label}: expected row {expected.Id}, got null.");
    if (actual.Id != expected.Id
        || actual.At != expected.At || actual.At.Offset != expected.At.Offset
        || actual.Duration != expected.Duration
        || actual.Token != expected.Token
        || !actual.Payload.AsSpan().SequenceEqual(expected.Payload)
        || actual.Priority != expected.Priority
        || actual.Status != expected.Status
        || actual.Score != expected.Score
        || actual.Ref != expected.Ref
        || actual.Sku != expected.Sku
        || actual.AltSku != expected.AltSku)
    {
        throw new InvalidOperationException(
            $"{label}: expected {expected} with payload {Convert.ToHexString(expected.Payload)}, " +
            $"got {actual} with payload {Convert.ToHexString(actual.Payload)}.");
    }
}
