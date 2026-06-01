# SQL migrations with `MigrationRunner`

ZeroAlloc.ORM 1.1+ ships a minimal SQL migration runner. Embed `.sql`
files in your assembly, instantiate `MigrationRunner`, call `RunAsync()`
at startup. The runner tracks applied versions in the `__zaorm_migrations`
table and skips already-applied migrations on subsequent runs. Idempotent
re-runs are a no-op.

The runner is **runtime** code (it lives in `ZeroAlloc.ORM`, not in the
generator), so it does not surface any `ZAO0xx` compile-time diagnostics.
Failure mode is a `DbException` propagated out of `RunAsync` — see
[Recipe 4](#recipe-4--failing-migrations).

## Recipe 1 — Embedded SQL migrations

The expected file layout:

```
src/
  MyApp/
    MyApp.csproj
    Program.cs
    Migrations/
      001_create_orders.sql
      002_add_customer_id.sql
      003_index_on_created.sql
```

In `MyApp.csproj`, opt the SQL files into the embedded-resource pipeline:

```xml
<ItemGroup>
  <EmbeddedResource Include="Migrations/*.sql" />
</ItemGroup>
```

In `Program.cs`, wire the three collaborators (connection, source, dialect)
and call `RunAsync`:

```csharp
using System.Data.Async;
using Microsoft.Data.Sqlite;
using ZeroAlloc.ORM.Migrations;

var raw = new SqliteConnection("Data Source=app.db");
IAsyncDbConnection conn = raw.AsAsync();
await conn.OpenAsync(ct).ConfigureAwait(false);

var source  = new EmbeddedResourceMigrationSource(typeof(Program).Assembly);
var dialect = new SqliteMigrationDialect();
var runner  = new MigrationRunner(conn, source, dialect);

var applied = await runner.RunAsync(ct).ConfigureAwait(false);
logger.LogInformation("Applied {Count} migrations", applied.Count);
```

The returned `IReadOnlyList<Migration>` contains **only** the migrations
newly applied during this call. Each carries the original `Version`,
`Name`, `Sql` plus a `AppliedAt` UTC timestamp the runner populates on
commit. A second `RunAsync` immediately after returns an empty list —
all migrations are already in the history table.

### File-naming convention

- Pattern: `NNN_description.sql`, where `NNN` is 3+ digits.
- Versions must be strictly increasing across all migrations.
- Gaps are permitted but **permanent**: if `003_x.sql` is already applied,
  a later-added `002_y.sql` will never run (its version is below the
  highest applied — the runner has no notion of "back-filling").
- Convention: zero-pad to at least 3 digits (`001` not `1`) so the files
  list in natural lexical order.
- The `description` segment becomes the `Name` stored in the history
  table — keep it `[A-Za-z0-9_]+` to satisfy the discovery regex.

### Scoping discovery in shared assemblies

The default `EmbeddedResourceMigrationSource` picks up **any** resource
whose name matches `*.Migrations.NNN_<name>.sql` anywhere in the assembly.
If multiple projects pile into one assembly (or you embed unrelated SQL
under a different folder), pass `resourceNamespacePrefix` to scope the
scan:

```csharp
var source = new EmbeddedResourceMigrationSource(
    assembly: typeof(Program).Assembly,
    resourceNamespacePrefix: "MyApp.Migrations.");
```

Only resources whose name starts with that literal prefix are considered.

## Recipe 2 — Provider selection (Sqlite vs Postgres)

The runner is identical across providers; the dialect picks the
provider-specific SQL templates + lock strategy. v1.1 ships two:

### Sqlite

```csharp
using Microsoft.Data.Sqlite;

var raw = new SqliteConnection("Data Source=app.db");
IAsyncDbConnection conn = raw.AsAsync();
await conn.OpenAsync(ct).ConfigureAwait(false);

var runner = new MigrationRunner(
    conn,
    new EmbeddedResourceMigrationSource(typeof(Program).Assembly),
    new SqliteMigrationDialect());

await runner.RunAsync(ct).ConfigureAwait(false);
```

The Sqlite dialect uses `INTEGER PRIMARY KEY` + `TEXT NOT NULL` columns
and stores `applied_at` as an ISO-8601 string (Sqlite has no native
timestamp type — see [`provider-quirks.md`](provider-quirks.md#decimal-stored-as-text)
for the matching `decimal`-as-text convention).

### Postgres

```csharp
using Npgsql;

var raw = new NpgsqlConnection(connString);
IAsyncDbConnection conn = raw.AsAsync();
await conn.OpenAsync(ct).ConfigureAwait(false);

var runner = new MigrationRunner(
    conn,
    new EmbeddedResourceMigrationSource(typeof(Program).Assembly),
    new PostgresMigrationDialect());

await runner.RunAsync(ct).ConfigureAwait(false);
```

The Postgres dialect uses `TIMESTAMPTZ NOT NULL DEFAULT NOW()` for
`applied_at` (preserves timezone info), and acquires
`pg_advisory_lock(<bigint>)` for the duration of the run — see
[Recipe 3](#recipe-3--multi-instance-startup).

## Recipe 3 — Multi-instance startup

Both dialects make multi-instance startup safe **without** the adopter
having to lease or coordinate from the outside:

- **Postgres** — `pg_advisory_lock(0x5A41_4F52_4D5F_4D49)` blocks until
  acquired at the start of `RunAsync`, and is released in a `finally`
  (or automatically on session termination). Two API instances starting
  simultaneously serialize at the advisory-lock call; the second instance
  enters the apply loop only after the first has committed and released
  the lock — by which point its applied-versions snapshot already shows
  everything done.
- **Sqlite** — no advisory lock is needed. Sqlite's single-writer model
  (BEGIN EXCLUSIVE / journal / WAL) serializes the per-migration
  transactions natively. Concurrent runners contend at the transaction
  layer; the loser blocks and re-reads the history table on its next
  iteration.

There is nothing for the adopter to configure — the lock strategy is
baked into the dialect.

## Recipe 4 — Failing migrations

When a migration's SQL throws, the runner:

1. Rolls back the **failing migration's own** transaction.
2. Leaves all **earlier** successfully-committed migrations in place
   (their transactions already committed, so the history table records
   them as applied).
3. Does **not** attempt subsequent migrations.
4. Releases the dialect's apply-lock (`finally` block — guaranteed even
   on exception).
5. Rethrows the original `DbException` verbatim — the message identifies
   the failing statement.

```csharp
try
{
    await runner.RunAsync(ct).ConfigureAwait(false);
}
catch (DbException ex)
{
    logger.LogError(ex,
        "Migration failed; database is consistent up to the last successful version");
    throw;
}
```

The recovery path is a **forward-fix migration**: write `004_fix_xxx.sql`
(or whichever version comes next), embed it, deploy, re-run. The runner
will skip every already-applied migration and apply only the new one.
Rolling back to a prior version is out of v1.1 scope — see
[When NOT to use this runner](#when-not-to-use-this-runner).

## Custom migration sources

`IMigrationSource` is a small interface:

```csharp
public interface IMigrationSource
{
    IReadOnlyList<Migration> GetMigrations();
}
```

Adopters whose migrations don't live as embedded resources (S3-hosted,
generated-at-build-time, etc.) implement it directly:

```csharp
public sealed class S3MigrationSource(IS3Client s3) : IMigrationSource
{
    public IReadOnlyList<Migration> GetMigrations()
    {
        // ... fetch SQL bodies from S3 ...
        return [
            new Migration(Version: 1, Name: "create_orders", Sql: createOrdersSql),
            new Migration(Version: 2, Name: "add_customer_id", Sql: addCustomerIdSql),
        ];
    }
}
```

The runner handles ordering, history-table filtering, and per-migration
transactions — the source is just a discovery + content-load function.

## Custom dialects (advanced)

`IMigrationDialect` is also small — three SQL strings (history-table
DDL, applied-version SELECT, INSERT-row SQL) and two lock hooks. v1.1
ships Sqlite + Postgres only. Adopters running on SQL Server, MySQL,
Oracle, or other providers implement the interface directly for now;
the SQL Server / MySQL ships are tracked as v1.1-CLN1 in the backlog.

```csharp
public sealed class SqlServerMigrationDialect : IMigrationDialect
{
    public string CreateHistoryTableSql => /* ... */;
    public string SelectAppliedVersionsSql => /* ... */;
    public string InsertAppliedVersionSql => /* ... */;
    public Task AcquireLockAsync(IAsyncDbConnection c, CancellationToken ct) => /* sp_getapplock ... */;
    public Task ReleaseLockAsync(IAsyncDbConnection c, CancellationToken ct) => /* sp_releaseapplock ... */;
}
```

## Custom advisory-lock key (Postgres)

The Postgres dialect defaults to `pg_advisory_lock(0x5A41_4F52_4D5F_4D49)`
— the ASCII bytes of `"ZAORM_MI"` packed into a `long`. If your process
already uses `pg_advisory_lock` for an unrelated purpose with a colliding
constant, pass a different key:

```csharp
var dialect = new PostgresMigrationDialect(lockKey: 0x12345678L);
```

The key is per-instance, not global — switching keys does not affect any
already-applied migrations; only the lock serialization.

## Provider quirks

- **Sqlite** — no advisory lock. Cross-process serialization happens
  through Sqlite's single-writer model (BEGIN EXCLUSIVE / journal / WAL).
  WAL mode is recommended for production. `applied_at` is stored as ISO-8601
  TEXT — see the Sqlite section of [`provider-quirks.md`](provider-quirks.md).
- **Postgres** — `pg_advisory_lock(<long>)` at `RunAsync` entry; released
  in a `finally`. Default key is `0x5A41_4F52_4D5F_4D49` (`"ZAORM_MI"`
  packed). Override via the `PostgresMigrationDialect(lockKey)` constructor.
  `applied_at` is stored as `TIMESTAMPTZ`.
- **SQL Server / MySQL / others** — not shipped in v1.1; implement
  `IMigrationDialect` directly. Tracked as v1.1-CLN1.

## When NOT to use this runner

- **You need rollback support.** Out of v1.1 scope; write a forward-fix
  migration instead.
- **You want a C# migration DSL** (FluentMigrator-style API surface).
  Out of v1.1 scope; this runner is raw SQL only.
- **You need migration squashing / branching** (multi-tenant schema
  divergence, feature-branch schemas). Out of v1.1 scope.
- **Your schema is one-shot** (greenfield dev / scratch DBs). An
  embedded `schema.sql` applied via `IAsyncDbCommand.ExecuteNonQueryAsync`
  is simpler. Migrations earn their weight only when you need versioned,
  idempotent, multi-instance-safe apply.

## Related cookbook recipes

- [`provider-quirks.md`](provider-quirks.md) — per-provider gotchas,
  including the Sqlite `decimal`-as-TEXT and `CanCreateBatch = false`
  story that constrain the Sqlite dialect's history-table types.
- [`observability.md`](observability.md) — wrap `MigrationRunner.RunAsync`
  in an `Activity` span via ZA.Telemetry's `[Instrument]` pattern.
- [`stored-procedures.md`](stored-procedures.md) — the natural pairing:
  migrations create the procedure, `[StoredProcedure]` calls it.

## Related diagnostics

The migration runner is runtime code, not generator code — no `ZAO0xx`
diagnostics fire from it. Failure mode is an exception raised at
`RunAsync()` (see [Recipe 4](#recipe-4--failing-migrations)).
