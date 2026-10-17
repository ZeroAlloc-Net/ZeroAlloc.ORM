# ZeroAlloc.ORM — EF database adoption, array and jsonb parameters

**Status:** design, awaiting approval · **Date:** 2026-10-17
**Origin:** Daedalus handover `Daedalus.NET/docs/plans/2026-10-17-zeroalloc-linq-handover.md`, "The three ORM gaps"
**Related:** [`2026-10-17-linq-design.md`](2026-10-17-linq-design.md). These gaps land first; the LINQ work's `text[]`,
`ANY(@array)` and jsonb goals depend on them.

## 1. Intent

Daedalus is moving from EF Core to ZeroAlloc.ORM to ship as Native AOT. Three gaps block that whatever the query layer
looks like:

1. **Adopting an existing database.** `MigrationRunner` only knows `__zaorm_migrations`. Daedalus' databases are
   managed by EF, with 26 migrations in the repo
   (`20260209134036_AddMissingTaskColumns` … `20261014090300_AddNodeSelectionUniqueIndex`) recorded in `__EFMigrationsHistory`. They must upgrade in place, with no data
   loss (owner decision).
2. **Array parameters and columns.** Daedalus has five `text[]` columns (Skill, SkillVersion, RoleCharterVersion,
   AgentMemory, Task), held as `List<string>` in the entities. They need read, write, and `= ANY(@array)` comparisons.
   The README lists array parameters as not supported.
3. **jsonb parameters.** Daedalus stores whole jsonb documents in `string` properties (`AgentMessage.ContentJson`,
   `AnalysisIteration.ValidationErrors`, `WorkflowRunRecord.PayloadJson`, `CodeAnalysisRequest.Requirements` / `ReferencedDocuments` /
   `ExternalReferences` / `ValidationResult`). Binding a plain string to a jsonb column fails in PostgreSQL ("column is
   of type jsonb but expression is of type text"). Today the workaround is a `::jsonb` cast in the SQL; the handover
   asks for a real parameter type.

All three are general: any EF adopter on PostgreSQL hits them.

## 2. Decisions made with the owner

1. **EF adoption is a generic baseline plus an EF helper.**
   - `MigrationRunner` gets a **baseline** option: a version `N` and a check. On a database with no ZeroAlloc history
     for the source being run, if the check passes, the runner records migrations `1..N` as applied **without running
     them**, then applies `N+1…` normally.
   - An **EF helper** supplies the check: "the **latest** id recorded in `__EFMigrationsHistory` is X" (ids sort as
     strings by their timestamp prefix). Daedalus'
     ZeroAlloc migration `N` is the schema as of its last EF migration. On an existing database the helper's check
     passes and `1..N` are recorded. A database at any other EF head, behind or ahead of X, is a loud error: bring it to
     X with EF first. On a fresh database `__EFMigrationsHistory` doesn't exist, so the check fails and
     `1..N` run normally.
   - The runner never reads or writes EF's table beyond that check, and never drops it.
2. **Provider-specific parameter types are emitted by the generator when Npgsql is referenced.** A marker (e.g.
   `[Json]`) on a parameter or column says what the value is. When the consumer's compilation references Npgsql, the
   generator emits code that sets `NpgsqlDbType` on the provider parameter. `AdoNet.Async` returns the provider's own
   parameter from `CreateParameter()`, so a type test reaches it. No new package, no runtime indirection. When Npgsql
   isn't referenced and the marker needs it, that's a diagnostic. ZeroAlloc.ORM's own assemblies keep **no** Npgsql
   dependency; only generated code in the consumer mentions it.

## 3. Goals

- **Baseline adoption:**
  - Running against a Daedalus database (37 EF migrations applied, no `__zaorm_migrations`) records `1..N` and applies
    only later migrations. Data is untouched.
  - Running against an empty database applies `1..N` as SQL and ends with the same schema.
  - Running a second time is a no-op.
  - `__EFMigrationsHistory` exists but its latest id isn't X: an error naming both ids. The runner never runs `1..N`
    over an EF-managed database. `__EFMigrationsHistory` absent: the database is treated as fresh and `1..N` run
    normally. The guard keys on EF's table, not on "any table exists", because other ZeroAlloc sources (Daedalus'
    `WorkflowOrmMigrations`, `OutboxOrmMigrations`) create their own tables first.
  - The baseline works with the multi-source scoping from #306: it applies per source, and only when that source has
    no recorded rows. Daedalus already runs other sources through `MigrationRunner`, and its `__zaorm_migrations`
    may predate scoping. The existing pre-scoping upgrade assigns such a table's rows to whichever source runs first,
    so a baseline source must not be the first to touch a pre-scoping table. Document that, and test a baseline source
    running after another source on the same database.
  - Postgres, Sqlite and SQL Server dialects; the EF helper reads EF's table name and `MigrationId` column, which are the
    same across EF providers.
- **Array parameters and columns** (PostgreSQL):
  - Parameters of type `T[]`, `List<T>`, `IReadOnlyList<T>` (at least for `string`, `int`, `long`, `Guid`) bind as
    PostgreSQL arrays, so `= ANY(@tags)`, `@tags::text[]`-free inserts and updates work.
  - Columns materialise into `string[]`, `List<string>` and `IReadOnlyList<string>` (and the same element types).
  - A collection parameter type that today means something else, such as bulk-insert row collections, keeps its
    meaning. Array binding must not change any existing classification; snapshot tests stay byte-identical.
  - Without Npgsql referenced, an array parameter is a diagnostic. Npgsql infers the array type from the CLR value, so
    the generator may not need to set `NpgsqlDbType` at all for arrays; whether it does is the builder's call, as long
    as the goal holds.
- **Npgsql under Native AOT:** the AOT path is `NpgsqlSlimDataSourceBuilder`, which needs `EnableArrays()` (and
  whatever jsonb-as-string needs) before these types work. Document the required calls, and run the array and jsonb
  integration tests on both a full and a slim data source.
- **jsonb parameters:** a `string` marked as JSON binds as `jsonb` with no cast in the SQL, and reads back as a string.
  `null` binds as SQL `NULL`.
- Integration tests for all of the above against a real PostgreSQL 16 (the existing Testcontainers fixture); the
  baseline also against Sqlite.
- Docs: a cookbook section "Adopting an EF Core database", array and jsonb entries in the type tables, and a page per
  new diagnostic.
- AOT-clean; additive public API (minor bump); `api-compat` green.

## 4. Non-goals

- Serialising objects to JSON. Daedalus holds JSON as strings; typed jsonb documents (with an AOT JSON serializer) can
  follow later.
- Querying inside jsonb, or `json` (non-b) support beyond what falls out for free.
- Multi-dimensional arrays, arrays of composites or enums.
- Translating EF migrations into ZeroAlloc migrations. Daedalus writes migration `N` (the baseline schema) once, for
  example from `dotnet ef migrations script`.
- Removing `__EFMigrationsHistory`.

## 5. Anti-goals

- Any path where the runner executes baseline migrations against a database that already has the schema.
- Any change to generated output or behaviour for existing consumers.
- A runtime Npgsql dependency in ZeroAlloc.ORM's packages.
- Reflection or suppressions to make provider typing work.

## 6. Calls I made (check these)

- **Marker shape:** `[Json]` in `ZeroAlloc.ORM.Abstractions`, usable on method parameters, record/ctor parameters
  (columns) and composite members. Arrays need no marker: their CLR type says it. The LINQ design's map classes reuse
  the same marker.
- **Non-Npgsql at run time:** if Npgsql is referenced but the connection at run time is another provider, the type test
  fails and the parameter binds as a plain string. Sqlite stores JSON as text, so tests on Sqlite keep working.
- **Baseline API:** an options object on `MigrationRunner`, roughly `Baseline(version, check)`, with
  `EfCoreBaseline.IfMigrationApplied("20261001_LastEfMigration")` as the shipped check. Exact names are the builder's.
- **Diagnostics:** new `ZAO` codes after the current highest (`ZAO081`), allocated up front. They stay outside the
  `ZAO100`–`ZAO199` block reserved for LINQ.
- **EF stays frozen after `N`.** Once Daedalus writes migration `N`, it adds no more EF migrations; the
  latest-id check turns any slip into a loud error rather than silent drift. Enforcing the freeze in Daedalus is
  Daedalus' job.
- **Deployed databases at older EF heads** are brought to X with EF's own `MigrateAsync` before the switch. The
  helper takes a single id, not a set.
- **Order and release:** three PRs, each shippable on its own: baseline, arrays, jsonb. They ship in the owner's next
  batched release cycle as a ZeroAlloc.ORM minor.

## 7. Left to the builder

- The exact detection of "already has tables" for the baseline mismatch guard, per dialect.
- Whether array binding needs `NpgsqlDbType` or relies on Npgsql's inference, and how empty arrays and `null` arrays
  bind.
- The generator classification changes, keeping existing shapes untouched.
- Test layout and docs wording.
