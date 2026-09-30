# SQL migrations with `MigrationRunner`

ZeroAlloc.ORM 1.1+ ships a minimal SQL migration runner. Embed `.sql`
files in your assembly, instantiate `MigrationRunner`, call `RunAsync()`
at startup. The runner tracks applied versions in the `__zaorm_migrations`
table, per migration source, and skips already-applied migrations on
subsequent runs. Several sources, such as your application's and a
library's, can migrate one database, each numbering from 1; see
[Several sources in one database](#several-sources-in-one-database). Idempotent
re-runs are a no-op — but only when the replayed migration matches on
BOTH version and name; see
[Version collisions](#version-collisions) below.

The runner is **runtime** code (it lives in `ZeroAlloc.ORM`, not in the
generator), so it does not surface any `ZAO0xx` compile-time diagnostics.
Failure mode is a `DbException` propagated out of `RunAsync` for a failing
migration body, or a `ZeroAllocOrmMigrationConflictException` for a version
collision — see [Recipe 4](#recipe-4--failing-migrations) and
[Version collisions](#version-collisions).

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
- Versions are per source: another source's version 1 does not collide
  with yours.
- Gaps are permitted. The runner applies every discovered version it has
  not recorded for the source, in ascending order, so a `002_y.sql` added
  after `003_x.sql` was applied still runs, after it. Number new
  migrations above the highest applied one so they run in the order you
  wrote them.
- `NNN` must be **unique** within the source, both against its
  already-applied versions and against every other migration discovered
  in the same run — see [Version collisions](#version-collisions).
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

The prefix also names the source in the history table: `MyApp.Migrations`
here, or the assembly's simple name when there is no prefix. See
[Several sources in one database](#several-sources-in-one-database).

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
- **SQL Server** — `sp_getapplock` on the `ZAORM_MIGRATIONS` resource,
  owned by the session, so it outlives each migration's own transaction.
  It is released in the same `finally`, or when the connection drops.
- **Sqlite** — no advisory lock is needed. Sqlite's single-writer model
  (BEGIN EXCLUSIVE / journal / WAL) serializes the per-migration
  transactions natively. Concurrent runners contend at the transaction
  layer; the loser blocks and re-reads the history table on its next
  iteration.

There is nothing for the adopter to configure — the lock strategy is
baked into the dialect.

## Recipe 4 — Failing migrations

When a migration's SQL throws, the runner:

1. Rolls back the **failing migration's own** transaction — see
   [When the rollback fails too](#when-the-rollback-fails-too) if that throws.
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

### When the rollback fails too

If rolling back the failing migration's transaction also throws (a dropped
connection, a provider that already aborted the transaction on its own), the
runner still rethrows the **original** exception unchanged: same type, same
message, same stack. The rollback exception is not discarded; it is stored in
the original exception's `Data` under `MigrationRunner.RollbackExceptionDataKey`.

Its presence means the rollback did not complete, so the database may be in an
unknown state. Inspect it before retrying:

```csharp
catch (DbException ex)
{
    if (ex.Data[MigrationRunner.RollbackExceptionDataKey] is Exception rollbackEx)
    {
        logger.LogCritical(rollbackEx,
            "Migration failed and its rollback failed too; verify the database state before retrying");
    }
    throw;
}
```

The entry is absent when the rollback succeeded. It is also not added when
the original exception's `Data` dictionary is read-only or fixed-size, so the
runner never replaces the original exception with an error of its own.

## Several sources in one database

Every `IMigrationSource` has a `Name`, and the history table records it with
each migration the source applies. The primary key is `(source, version)`, so
each source numbers its migrations on its own. Your application's migrations
and a library's, such as the ZeroAlloc.Saga or ZeroAlloc.Outbox schema, can
both start at version 1 and migrate the same database:

```csharp
await new MigrationRunner(conn, new EmbeddedResourceMigrationSource(typeof(Program).Assembly), dialect)
    .RunAsync(ct).ConfigureAwait(false);
await new MigrationRunner(conn, SagaOrmMigrations.Postgres, dialect)
    .RunAsync(ct).ConfigureAwait(false);
await new MigrationRunner(conn, OutboxOrmMigrations.Postgres, dialect)
    .RunAsync(ct).ConfigureAwait(false);
```

The name is how the runner finds a source's applied versions, so it must stay
the same once the source has run. Changing it makes the runner treat the source
as new and apply its migrations again.

| Source | Default `Name` |
|--------|----------------|
| `EmbeddedResourceMigrationSource` | `resourceNamespacePrefix` without its trailing dot, such as `MyApp.Migrations`; the assembly's simple name when there is no prefix. Set `Name` in an object initializer to fix it: `new EmbeddedResourceMigrationSource(asm) { Name = "MyApp" }`. |
| Your own `IMigrationSource` | The type's name, such as `MyApp.Data.AppMigrations`. Override `Name` to fix it. |

A library that ships a source should return a fixed string from `Name`, so
renaming or moving its type later cannot change the name. The name must not be
empty, and is at most `MigrationRunner.MaxSourceNameLength`, 256, characters;
the `MigrationRunner` constructor throws `ArgumentException` otherwise.

### Upgrading a history table from before source scoping

Before source scoping, the history table kept one version sequence for the
whole database, keyed by `version` alone. The first `RunAsync` against such a
table upgrades it in place, on every built-in dialect:

1. Inside the apply-lock, it opens one transaction.
2. It adds the `source` column, assigns **every existing row to the source
   being run**, and makes `(source, version)` the primary key. SQLite cannot
   change a primary key, so there the table is copied into the new layout and
   renamed into place. Recorded names and `applied_at` values are kept.
3. It checks that the rows belong to that source: every row must be one of
   the source's migrations, with the same version and name.
4. It commits, and the run continues as usual. Later runs find the table
   already scoped and skip the upgrade.

If the check fails, or any step throws, the transaction rolls back and the
table is left exactly as it was. The check failing raises
`ZeroAllocOrmMigrationConflictException`, which names the source being run
and lists the rows that are not its migrations.

The table was written by one source before, so **run that source first** after
upgrading ZeroAlloc.ORM: the one whose migrations are recorded in it. If you
combined several sources into one `IMigrationSource`, for example by adding an
offset to one library's versions, that combined source wrote the rows. Keep
running it, under the same name, rather than switching to the separate sources:
they would find no rows of their own and apply their migrations again.

A rolled-back upgrade needs no clean-up: run the right source and it upgrades
the table.

#### When the table holds more than one source's rows

Before scoping, two sources could share the table if their version numbers
did not overlap, for example your application's 1 to 12 and a library's 1000.
The check refuses to upgrade such a table from either source, because
assigning the library's row to your application would make the library apply
its migration again. The same happens when a source has since dropped a
migration it once recorded. The runner cannot tell whose rows those are, so
assign them yourself, once, in a transaction, before the first run on the new
version. For PostgreSQL:

```sql
BEGIN;
ALTER TABLE __zaorm_migrations RENAME TO __zaorm_migrations_old;
ALTER INDEX __zaorm_migrations_pkey RENAME TO __zaorm_migrations_old_pkey;
-- PostgresMigrationDialect.CreateHistoryTableSql:
CREATE TABLE __zaorm_migrations (source TEXT NOT NULL, version INTEGER NOT NULL,
  name TEXT NOT NULL, applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  PRIMARY KEY (source, version));
INSERT INTO __zaorm_migrations (source, version, name, applied_at)
  SELECT CASE WHEN version >= 1000 THEN 'MyLibrary' ELSE 'MyApp.Migrations' END,
         CASE WHEN version >= 1000 THEN version - 1000 ELSE version END,
         name, applied_at
  FROM __zaorm_migrations_old;
DROP TABLE __zaorm_migrations_old;
COMMIT;
```

Here the library's versions had been offset by 1000 to share the table, so
they are mapped back to its own numbering; its source would not recognise
them otherwise. Use each source's `Name` exactly as the runner will see it.
On another database, create the table with that dialect's
`CreateHistoryTableSql` and copy the rows the same way.

## Version collisions

The history table records the `source`, the `version` and the `name` of every
applied migration. Collisions are checked within one source only; another
source's versions are never compared. `RunAsync` uses the version and the name
to tell a genuine
**replay** (safe to skip) from a **version collision** (a bug — the runner
throws `ZeroAllocOrmMigrationConflictException` instead of silently
dropping the migration):

- **Replay** — a discovered migration's `Version` AND `Name` match a row
  the source already recorded. Silent no-op, as always.
- **Applied-version collision** — a discovered migration's `Version`
  matches a row the source recorded, but its `Name` does NOT. This
  happens when two developers each add a "next" migration independently
  and both land on the same `NNN` prefix, or when a migration is
  renumbered after it already shipped. The runner throws, naming the
  version, the recorded name, and the discovered name. **Resolution:
  renumber the new migration** to an unused version — never reuse a
  version number that has already been applied.
- **In-run collision** — two migrations discovered in the *same* call to
  `GetMigrations()` share a `Version`, regardless of whether either has
  been applied yet (e.g. two files both named `007_*.sql`). The runner
  throws before attempting to apply anything. **Resolution: renumber one
  of the two files.**

Detecting a name-mismatched collision requires the runner to read `name`
alongside `version`, which is a breaking change for a custom
`IMigrationDialect` — see
[Migrating to v2](../migrating-to-v2.md#imigrationdialectselectappliedversionssql-must-return-version-name)
if you implement the interface directly rather than using a shipped
dialect.

```csharp
try
{
    await runner.RunAsync(ct).ConfigureAwait(false);
}
catch (ZeroAllocOrmMigrationConflictException ex)
{
    logger.LogError(ex, "Migration version collision — renumber the new migration");
    throw;
}
```

## Recipe 5 — Test fixtures over a kept-alive in-memory connection

Integration tests against a real database typically reuse one
`SqliteConnection("DataSource=:memory:")` for the lifetime of the fixture
— the `:memory:` database is bound to that one connection, so closing it
drops the schema. Applying migrations once in the fixture ctor against
that connection and sharing the resulting `IAsyncDbConnection` across
every test in the suite is the natural pattern:

```csharp
public sealed class IntegrationFixture : IAsyncDisposable
{
    private readonly SqliteConnection _raw = new("DataSource=:memory:");

    public IntegrationFixture()
    {
        _raw.Open();
        AsyncConnection = _raw.AsAsync();

        var runner = new MigrationRunner(
            AsyncConnection,
            new EmbeddedResourceMigrationSource(typeof(IntegrationFixture).Assembly),
            new SqliteMigrationDialect());
        runner.RunAsync().GetAwaiter().GetResult();
    }

    public IAsyncDbConnection AsyncConnection { get; }

    public async ValueTask DisposeAsync()
    {
        await AsyncConnection.DisposeAsync().ConfigureAwait(false);
        _raw.Dispose();
    }
}
```

### The DI scope-disposal trap

If the fixture also hosts the application (e.g. via
`WebApplicationFactory<TProgram>` for endpoint tests), you'll need to
register the shared `IAsyncDbConnection` in the test's service
collection so the application's repositories resolve against it.
Register as **singleton, not scoped**:

```csharp
services.AddSingleton<IAsyncDbConnection>(_ => fixture.AsyncConnection);  // ✅ correct
services.AddScoped<IAsyncDbConnection>(_ => fixture.AsyncConnection);     // ❌ wrong — closes the DB
```

The reason: `Microsoft.Extensions.DependencyInjection` tracks every
`IAsyncDisposable` resolved through a scoped factory delegate and adds
it to the scope's disposal list. End of the first request scope calls
`DisposeAsync` on the wrapper, which closes the underlying
`SqliteConnection` — and `:memory:` evaporates with it. The second
request fails with `SqliteException: 'no such table: ...'`.

Singleton-by-factory still tracks for disposal, but only at host
shutdown (i.e. fixture teardown), where your own
`fixture.DisposeAsync()` already closes the connection.
`SqliteConnection` tolerates the resulting double-dispose, so the two
disposal paths coexist safely.

The trap does **not** apply to production wiring where the factory
delegate `new`s a fresh provider connection per scope
(`new NpgsqlConnection(connStr).AsAsync()`). There, scope disposal
correctly returns the underlying physical connection to the provider's
pool — which is what you want. The fix is specifically for **test
fixtures that share a single kept-alive wrapper** across DI scopes.

## Custom migration sources

`IMigrationSource` is a small interface:

```csharp
public interface IMigrationSource
{
    string Name => GetType().ToString();   // scopes the source's versions
    IReadOnlyList<Migration> GetMigrations();
}
```

Adopters whose migrations don't live as embedded resources (S3-hosted,
generated-at-build-time, etc.) implement it directly:

```csharp
public sealed class S3MigrationSource(IS3Client s3) : IMigrationSource
{
    public string Name => "MyApp";

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
DDL, applied-version SELECT, INSERT-row SQL) and two lock hooks. The
SQLite, PostgreSQL and SQL Server dialects ship with ZeroAlloc.ORM.
Adopters running on MySQL, Oracle, or other providers implement the
interface directly; a MySQL dialect is tracked as v1.1-CLN1 in the backlog.

A dialect that implements only `IMigrationDialect` keeps one version
sequence per database, as before source scoping: its SQL has no `source`
column and the runner binds no `source` parameter. To scope versions by
source, implement `IScopedMigrationDialect` as well, as the shipped
dialects do:

- `CreateHistoryTableSql` creates a `source` column, part of the primary
  key with `version`.
- `SelectAppliedVersionsSql` filters by `@source`, and
  `InsertAppliedVersionSql` writes it; the runner binds `source`.
- `SelectUnscopedHistorySql` returns a non-zero value when the table has
  no `source` column, and `UpgradeUnscopedHistorySql` lists the statements
  that upgrade it, each run with `source` bound, in one transaction. See
  [Upgrading a history table](#upgrading-a-history-table-from-before-source-scoping).

`SelectAppliedVersionsSql` **must** select `version, name` (in that
column order) — `MigrationRunner` reads both columns positionally to
tell a replay from a version collision; see
[Version collisions](#version-collisions). `InsertAppliedVersionSql`
already writes `name` into the history table, so this is a read-side
change only — no new column, no migration of your own history table.

```csharp
public sealed class OracleMigrationDialect : IMigrationDialect
{
    public string CreateHistoryTableSql => /* ... */;
    public string SelectAppliedVersionsSql =>
        "SELECT version, name FROM zaorm_migrations ORDER BY version";
    public string InsertAppliedVersionSql => /* ... */;
    public Task AcquireLockAsync(IAsyncDbConnection c, CancellationToken ct) => /* DBMS_LOCK.REQUEST ... */;
    public Task ReleaseLockAsync(IAsyncDbConnection c, CancellationToken ct) => /* DBMS_LOCK.RELEASE ... */;
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
`RunAsync()`: a `DbException` for a failing migration body (see
[Recipe 4](#recipe-4--failing-migrations)), or a
`ZeroAllocOrmMigrationConflictException` for a version collision (see
[Version collisions](#version-collisions)).
