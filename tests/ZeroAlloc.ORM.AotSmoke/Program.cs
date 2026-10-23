// AOT smoke consumer for ZeroAlloc.ORM. Exercises both emitted shapes
// (scalar int and FlatRow) against in-memory Sqlite, plus DateOnly and TimeOnly
// parameters, columns and scalars (#256), and value-type shapes: GetFieldValue<T>
// columns, enums in int and string storage, nullable primitives and user structs. CI publishes this with
// PublishAot=true and runs the native binary — any trimmer warning escalates
// to an error via TreatWarningsAsErrors at the repo root, and a non-zero exit
// code fails the workflow.

using Microsoft.Data.Sqlite;
using Npgsql;
using System.Data.Async;
using System.Data.Async.Adapters;
using System.Globalization;
using ZeroAlloc.ORM;
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

await RunPostgresAsync().ConfigureAwait(false);

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

// PostgreSQL arrays and jsonb strings through a slim data source, which needs EnableArrays. Skipped
// without a server; the publish still trims and analyses the generated code either way.
static async Task RunPostgresAsync()
{
    var cs = Environment.GetEnvironmentVariable("ZAORM_AOT_PG");
    if (string.IsNullOrEmpty(cs))
    {
        Console.WriteLine("Postgres smoke skipped: ZAORM_AOT_PG is not set.");
        return;
    }

    var dataSource = new NpgsqlSlimDataSourceBuilder(cs).EnableArrays().Build();
    await using (dataSource.ConfigureAwait(false))
    {
        var pg = dataSource.CreateConnection();
        await using (pg.ConfigureAwait(false))
        {
            await pg.OpenAsync().ConfigureAwait(false);
            IAsyncDbConnection connection = pg.AsAsync();
            await using (connection.ConfigureAwait(false))
            {
                var ddl = connection.CreateCommand();
                await using (ddl.ConfigureAwait(false))
                {
                    ddl.CommandText = "CREATE TABLE tagged (id uuid PRIMARY KEY, tags text[] NOT NULL);";
                    await ddl.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                var repo = new ArrayRepo(connection);
                var a = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");
                var b = new Guid("7c9e6679-7425-40de-944b-e07fc1f90ae7");
                await repo.InsertAsync(a, ["x", "y"], CancellationToken.None).ConfigureAwait(false);
                await repo.InsertAsync(b, ["z"], CancellationToken.None).ConfigureAwait(false);

                var rows = await repo.GetByIdsAsync([b], CancellationToken.None).ConfigureAwait(false);
                if (rows.Count != 1 || rows[0].Id != b || !rows[0].Tags.AsSpan().SequenceEqual(["z"]))
                    throw new InvalidOperationException($"Unexpected array rows: {rows.Count}.");

                var list = await repo.GetListAsync(a, CancellationToken.None).ConfigureAwait(false);
                if (list is null || !list.Tags.SequenceEqual(["x", "y"], StringComparer.Ordinal))
                    throw new InvalidOperationException("Unexpected List<string> column.");

                await RunFloatArrayAsync(connection, repo, a).ConfigureAwait(false);
                Console.WriteLine("Postgres array smoke passed.");

                await RunJsonAsync(connection).ConfigureAwait(false);

                await RunLinqAsync(connection).ConfigureAwait(false);

                await RunLinqWritesAsync(connection, dataSource).ConfigureAwait(false);
            }
        }
    }
}

// jsonb strings: [Json] sets NpgsqlDbType.Jsonb, so no cast is needed in the SQL.
static async Task RunJsonAsync(IAsyncDbConnection connection)
{
    var ddlJson = connection.CreateCommand();
    await using (ddlJson.ConfigureAwait(false))
    {
        ddlJson.CommandText = "CREATE TABLE documents (id uuid PRIMARY KEY, body jsonb NOT NULL, note jsonb NULL);";
        await ddlJson.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    var json = new JsonRepo(connection);
    var d = new Guid("1b4e28ba-2fa1-11d2-883f-0016d3cca427");
    await json.InsertAsync(d, "{\"a\": 1}", null, CancellationToken.None).ConfigureAwait(false);
    var doc = await json.GetAsync(d, CancellationToken.None).ConfigureAwait(false);
    if (doc is null || doc.Id != d || !string.Equals(doc.Body, "{\"a\": 1}", StringComparison.Ordinal) || doc.Note is not null)
        throw new InvalidOperationException("Unexpected jsonb row.");
    await json.InsertAsync(Guid.NewGuid(), "{\"a\": 2}", "[1]", CancellationToken.None).ConfigureAwait(false);
    Console.WriteLine("Postgres jsonb smoke passed.");
}

// A float[] parameter binds as real[] and the column reads back as float[].
static async Task RunFloatArrayAsync(IAsyncDbConnection connection, ArrayRepo repo, Guid id)
{
    var ddl = connection.CreateCommand();
    await using (ddl.ConfigureAwait(false))
    {
        ddl.CommandText = "CREATE TABLE vectors (id uuid PRIMARY KEY, embedding real[] NOT NULL);";
        await ddl.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    await repo.InsertVectorAsync(id, [0.25f, -1.5f, 3f], CancellationToken.None).ConfigureAwait(false);
    var vector = await repo.GetVectorAsync(id, CancellationToken.None).ConfigureAwait(false);
    if (vector is null || !vector.Embedding.AsSpan().SequenceEqual([0.25f, -1.5f, 3f]))
        throw new InvalidOperationException("Unexpected float[] column.");
}

// [LinqQuery] through a map with a schema, column overrides and an EnumAsString column: a filter
// with an optional parameter, ordering, and a count.
static async Task RunLinqAsync(IAsyncDbConnection connection)
{
    var ddl = connection.CreateCommand();
    await using (ddl.ConfigureAwait(false))
    {
        ddl.CommandText = "CREATE TABLE public.linq_items (id uuid PRIMARY KEY, name text NOT NULL, rank integer NOT NULL, priority text NOT NULL, status text NOT NULL);";
        await ddl.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    var linq = new LinqRepo(connection);
    var high = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
    var low = new Guid("6ba7b811-9dad-11d1-80b4-00c04fd430c8");
    await linq.InsertAsync(high, "alpha", 5, "High", Status.Active, CancellationToken.None).ConfigureAwait(false);
    await linq.InsertAsync(low, "beta", 3, "Low", Status.Active, CancellationToken.None).ConfigureAwait(false);
    await linq.InsertAsync(Guid.NewGuid(), "gamma", 9, "High", Status.Archived, CancellationToken.None).ConfigureAwait(false);
    await linq.InsertAsync(Guid.NewGuid(), "delta", 1, "Normal", Status.Active, CancellationToken.None).ConfigureAwait(false);

    var all = await linq.ActiveAsync(2, null, CancellationToken.None).ConfigureAwait(false);
    if (all.Count != 2 || all[0].Id != high || all[1].Id != low || all[0].Priority != Priority.High || all[1].Priority != Priority.Low)
        throw new InvalidOperationException($"Unexpected LINQ rows: {all.Count}.");

    var lows = await linq.ActiveAsync(2, Priority.Low, CancellationToken.None).ConfigureAwait(false);
    if (lows.Count != 1 || lows[0].Id != low || !string.Equals(lows[0].Name, "beta", StringComparison.Ordinal))
        throw new InvalidOperationException($"Unexpected filtered LINQ rows: {lows.Count}.");

    var count = await linq.CountActiveAsync(2, CancellationToken.None).ConfigureAwait(false);
    if (count != 2)
        throw new InvalidOperationException($"Unexpected LINQ count: {count}.");

    Console.WriteLine("Postgres LINQ smoke passed.");

    await RunLinqJoinAsync(connection).ConfigureAwait(false);
}

// A grouped inner join and a scalar Average: tokens per task title, and the mean over all runs.
static async Task RunLinqJoinAsync(IAsyncDbConnection connection)
{
    var ddl = connection.CreateCommand();
    await using (ddl.ConfigureAwait(false))
    {
        ddl.CommandText = "CREATE TABLE public.smoke_tasks (id uuid PRIMARY KEY, title text NOT NULL, origin_url text NULL, \"Origin_Branch\" text NULL); "
            + "CREATE TABLE public.smoke_runs (id uuid PRIMARY KEY, task_id uuid NOT NULL, tokens integer NOT NULL);";
        await ddl.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    var repo = new LinqJoinRepo(connection);
    var one = Guid.NewGuid();
    var two = Guid.NewGuid();
    await repo.InsertTaskAsync(one, "build", CancellationToken.None).ConfigureAwait(false);
    await repo.InsertTaskAsync(two, "test", CancellationToken.None).ConfigureAwait(false);
    await repo.InsertRunAsync(Guid.NewGuid(), one, 10, CancellationToken.None).ConfigureAwait(false);
    await repo.InsertRunAsync(Guid.NewGuid(), one, 20, CancellationToken.None).ConfigureAwait(false);
    await repo.InsertRunAsync(Guid.NewGuid(), two, 5, CancellationToken.None).ConfigureAwait(false);

    var rows = await repo.TokensPerTaskAsync(CancellationToken.None).ConfigureAwait(false);
    if (rows.Count != 2
        || !string.Equals(rows[0].Title, "build", StringComparison.Ordinal) || rows[0].Tokens != 30 || rows[0].Runs != 2
        || !string.Equals(rows[1].Title, "test", StringComparison.Ordinal) || rows[1].Tokens != 5 || rows[1].Runs != 1)
        throw new InvalidOperationException($"Unexpected grouped join rows: {rows.Count}.");

    var average = await repo.AverageTokensAsync(CancellationToken.None).ConfigureAwait(false);
    if (Math.Abs(average - 35.0 / 3.0) > 1e-9)
        throw new InvalidOperationException($"Unexpected LINQ average: {average}.");

    Console.WriteLine("Postgres LINQ join smoke passed.");

    await RunLinqIncludeAsync(connection, one, two).ConfigureAwait(false);
}

// A page of tasks with the runs of that page, in one batch. "build" and "test" have no origin, so
// their owned members read NULL; "deploy" has one, whose Url reads back.
static async Task RunLinqIncludeAsync(IAsyncDbConnection connection, Guid build, Guid test)
{
    var repo = new LinqIncludeRepo(connection);
    var deploy = Guid.NewGuid();
    await repo.InsertTaskAsync(deploy, "deploy", "https://example.com/r", null, CancellationToken.None).ConfigureAwait(false);
    await new LinqJoinRepo(connection).InsertRunAsync(Guid.NewGuid(), deploy, 7, CancellationToken.None).ConfigureAwait(false);

    // By title: build, deploy, test. The first page holds build and deploy, with their runs only.
    var (tasks, runs) = await repo.PagedTasksWithRunsAsync(0, 2, CancellationToken.None).ConfigureAwait(false);
    if (tasks.Count != 2
        || tasks[0] != new SmokeTaskHead(build, "build", null, null)
        || tasks[1] != new SmokeTaskHead(deploy, "deploy", "https://example.com/r", null))
        throw new InvalidOperationException($"Unexpected paged parents: {tasks.Count}.");
    if (runs.Count != 3 || runs[0] != new SmokeRunLine(deploy, 7) || runs[1] != new SmokeRunLine(build, 10) || runs[2] != new SmokeRunLine(build, 20))
        throw new InvalidOperationException($"Unexpected paged children: {runs.Count}.");

    var (last, lastRuns) = await repo.PagedTasksWithRunsAsync(2, 2, CancellationToken.None).ConfigureAwait(false);
    if (last.Count != 1 || last[0].Id != test || lastRuns.Count != 1 || lastRuns[0] != new SmokeRunLine(test, 5))
        throw new InvalidOperationException($"Unexpected last page: {last.Count}, {lastRuns.Count}.");

    Console.WriteLine("Postgres LINQ include smoke passed.");
}

// [LinqCommand] and a row lock over the linq_items rows RunLinqAsync left: alpha (rank 5), beta (3)
// and delta (1) are active, gamma (9) is archived. An update guarded by xmin goes through with the
// version it read and throws once that version is stale; two transactions claiming one row each
// with SKIP LOCKED get different rows; a delete removes the archived row.
static async Task RunLinqWritesAsync(IAsyncDbConnection connection, NpgsqlDataSource dataSource)
{
    var repo = new LinqWriteRepo(connection);
    var alpha = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
    var beta = new Guid("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    var read = await repo.GetVersionAsync(alpha, CancellationToken.None).ConfigureAwait(false)
        ?? throw new InvalidOperationException("The row to promote is missing.");
    await repo.PromoteAsync(alpha, read.Version, CancellationToken.None).ConfigureAwait(false);
    var promoted = await repo.GetVersionAsync(alpha, CancellationToken.None).ConfigureAwait(false);
    if (promoted is null || promoted.Rank != 6 || promoted.Version == read.Version)
        throw new InvalidOperationException($"Unexpected promoted row: rank {promoted?.Rank}.");

    var stale = false;
    try
    {
        await repo.PromoteAsync(alpha, read.Version, CancellationToken.None).ConfigureAwait(false);
    }
    catch (ZeroAllocOrmConcurrencyException)
    {
        stale = true;
    }
    var after = await repo.GetVersionAsync(alpha, CancellationToken.None).ConfigureAwait(false);
    if (!stale || after is null || after.Rank != 6 || after.Version != promoted.Version)
        throw new InvalidOperationException($"A stale xmin did not throw, or it wrote: rank {after?.Rank}.");

    var otherPg = dataSource.CreateConnection();
    await using (otherPg.ConfigureAwait(false))
    {
        await otherPg.OpenAsync().ConfigureAwait(false);
        IAsyncDbConnection otherConnection = otherPg.AsAsync();
        await using (otherConnection.ConfigureAwait(false))
        {
            var tx = await connection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                var otherTx = await otherConnection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
                await using (otherTx.ConfigureAwait(false))
                {
                    var first = await repo.ClaimAsync(Status.Active, 1, tx, CancellationToken.None).ConfigureAwait(false);
                    var second = await new LinqWriteRepo(otherConnection)
                        .ClaimAsync(Status.Active, 1, otherTx, CancellationToken.None).ConfigureAwait(false);
                    if (first.Count != 1 || first[0].Rank != 1 || second.Count != 1 || second[0].Id != beta)
                        throw new InvalidOperationException($"Unexpected SKIP LOCKED claims: {first.Count}, {second.Count}.");
                    await otherTx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    var deleted = await repo.DeleteByStatusAsync(Status.Archived, CancellationToken.None).ConfigureAwait(false);
    var left = await new LinqRepo(connection).CountActiveAsync(0, CancellationToken.None).ConfigureAwait(false);
    if (deleted != 1 || left != 3)
        throw new InvalidOperationException($"Unexpected LINQ delete: {deleted} deleted, {left} left.");

    Console.WriteLine("Postgres LINQ write smoke passed.");
}
