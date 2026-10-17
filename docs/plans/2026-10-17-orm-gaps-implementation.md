# EF database adoption, array and jsonb parameters — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the three ZeroAlloc.ORM gaps that block Daedalus' move off EF Core: a `MigrationRunner` baseline with an
EF-history check, PostgreSQL array parameters and columns, and `[Json]` string parameters that bind as jsonb.

**Architecture:** Three independent PRs, each its own branch off `main`, in this order. PR 1 is runtime-only
(`src/ZeroAlloc.ORM/Migrations`). PRs 2 and 3 are generator changes: a new `ConventionKind.PrimitiveArray` in
`ZeroAlloc.TypeConversions`, and a `[Json]` marker in `ZeroAlloc.ORM.Abstractions`. The generator emits Npgsql-specific
code only when the consumer's compilation contains `Npgsql.NpgsqlParameter`. ZeroAlloc.ORM's own packages never reference
Npgsql.

**Tech Stack:** .NET 10 SDK, Roslyn incremental generator (`netstandard2.0`, Microsoft.CodeAnalysis.CSharp 5.9.0),
AdoNet.Async, Npgsql 10.0.3 (tests only), xunit.v3 + AwesomeAssertions, Testcontainers PostgreSQL 16, GeneratorSnapshot
from ZeroAlloc.TestHelpers.

**Spec:** `docs/design/2026-10-17-orm-gaps-design.md`

## Global Constraints

- Every existing generator snapshot stays byte-identical; no existing test changes its expectation.
- Additive public API only: new entries go in `PublicAPI.Unshipped.txt`; `api-compat` must stay green. Never add an
  optional parameter to a shipped signature; add an overload instead.
- No Npgsql `PackageReference` in any `src/` project.
- AOT-clean: zero trim/AOT warnings in `aot-smoke`; no `#pragma`, `SuppressMessage` or `UnconditionalSuppressMessage`.
- New diagnostic IDs: `ZAO082` (provider-specific type requires Npgsql) and `ZAO083` (`[Json]` on a non-string). They
  stay outside the `ZAO100`–`ZAO199` block reserved for LINQ. Each one needs a descriptor, a `LookupDescriptor` entry
  (`OrmGenerator.cs:4503-4548`, or it is silently dropped), a row in `AnalyzerReleases.Unshipped.md`, and a
  `docs/diagnostics/ZAOxxx.md` page plus a row in `docs/diagnostics/README.md`.
- Conventional-commit PR titles (`feat:`, `docs:`, …). Commit bodies: no nested parentheses, lines ≤ 100 chars.
- Agents VM: run one test project at a time with a filter, never the whole solution in parallel.
- The design docs on branch `docs/linq-design` go up as their own `docs:` PR. The three PRs below branch from `main`.

## Review Focus

1. **A pre-scoping `__zaorm_migrations` plus a baseline source.** Daedalus already runs Workflow and Outbox sources.
   The baseline source must apply only when *its own* scoped rows are empty, and must not disturb the other sources'
   rows. Test: Task 2's `Baseline_source_after_another_source_marks_only_its_own_rows`.
2. **Baseline version absent from the source.** `throughVersion = 26` with no migration 26 must fail before anything is
   written, not silently mark fewer rows. Test: Task 1's
   `Baseline_version_missing_from_source_throws_before_writing`.
3. **`null` and empty arrays.** A `null` `string[]` must bind as SQL `NULL`. An empty array must bind as `'{}'`, so
   `= ANY(@p)` matches nothing and doesn't throw. Test: Task 5's `Null_and_empty_arrays_round_trip`.
4. **An `IReadOnlyList<string>` that is neither an array nor a `List<T>`** (e.g. `ImmutableArray<string>` boxed, or a
   `ReadOnlyCollection<string>`) must still bind. Test: Task 5's `ReadOnlyCollection_parameter_binds_as_array`.
5. **jsonb `null` and invalid JSON.** A `null` `[Json] string?` must store SQL `NULL`. Invalid JSON must surface as
   PostgreSQL's own error (`22P02`), not be swallowed. Test: Task 7's `Null_json_stores_sql_null` and
   `Invalid_json_surfaces_postgres_error`.

---

## PR 1 — `feat: adopt an existing EF Core database with a migration baseline`

Branch: `feat/migration-baseline` off `main`.

### Task 1: `MigrationBaseline` and the runner's baseline step

**Files:**
- Create: `src/ZeroAlloc.ORM/Migrations/MigrationBaseline.cs`
- Create: `src/ZeroAlloc.ORM/Migrations/IMigrationBaselineCheck.cs`
- Create: `src/ZeroAlloc.ORM/Migrations/BaselineDecision.cs`
- Modify: `src/ZeroAlloc.ORM/Migrations/MigrationRunner.cs` (new ctor overload after :89; new step between :177 and :178)
- Modify: `src/ZeroAlloc.ORM/PublicAPI.Unshipped.txt`
- Test: `tests/ZeroAlloc.ORM.Integration.Tests/Migrations/MigrationRunnerBaselineSqliteTests.cs`

**Interfaces:**
- Produces:
  - `public enum BaselineDecision { RunAll = 0, MarkApplied = 1 }`
  - `public interface IMigrationBaselineCheck { Task<BaselineDecision> DecideAsync(IAsyncDbConnection connection, CancellationToken ct); }`
    Throws `ZeroAllocOrmMigrationConflictException` when the database must not be touched.
  - `public sealed class MigrationBaseline` with ctor `MigrationBaseline(int throughVersion, IMigrationBaselineCheck check)`
    (`throughVersion < 1` → `ArgumentOutOfRangeException`; null check → `ArgumentNullException`), and get-only properties
    `int ThroughVersion`, `IMigrationBaselineCheck Check`.
  - `public MigrationRunner(IAsyncDbConnection connection, IMigrationSource source, IMigrationDialect dialect, MigrationBaseline baseline)`.
    The existing three-argument ctor is unchanged and means "no baseline".

- [ ] **Step 1: Write the failing tests.** In `MigrationRunnerBaselineSqliteTests`, use `SqliteFixture` and a private
  `ListMigrationSource`, copied from `MigrationRunnerSqliteTests.cs:278`, with migrations 1–3 that each create a table
  `t1`, `t2`, `t3`. Use a test `FixedCheck(BaselineDecision)` and a `ThrowingCheck` implementing
  `IMigrationBaselineCheck`.
  - `MarkApplied_records_through_version_without_running_them`: pre-create `t1`/`t2` by hand. Use baseline(2, MarkApplied).
    The result contains only version 3. `__zaorm_migrations` has versions 1,2,3 for the source. `t3` exists.
  - `RunAll_runs_every_migration`: empty db, baseline(2, RunAll). The result has versions 1,2,3.
  - `Second_run_is_a_noop_and_does_not_call_the_check`: run twice. The second result is empty. A counting check is
    called once.
  - `Check_is_skipped_when_source_already_has_history`: run without baseline first, then with a `ThrowingCheck`. It
    doesn't throw.
  - `Check_exception_propagates_and_writes_nothing`: a `ThrowingCheck` on an empty db. It throws
    `ZeroAllocOrmMigrationConflictException`, and `__zaorm_migrations` has no rows for the source.
  - `Baseline_version_missing_from_source_throws_before_writing`: baseline(5, MarkApplied) with a source of 1–3. It throws
    `ZeroAllocOrmMigrationConflictException` whose message contains `"5"`, the check is not called, and there are no
    history rows.
- [ ] **Step 2: Run them; expect a compile failure** (types missing).
  Run: `dotnet test tests/ZeroAlloc.ORM.Integration.Tests --filter "FullyQualifiedName~MigrationRunnerBaselineSqliteTests"`
- [ ] **Step 3: Implement the three types and the ctor overload.** Validate `ThroughVersion` against the discovered
  migrations right after `RejectDuplicateVersions` (:208), before any write.
- [ ] **Step 4: Implement the baseline step.** After `ReadAppliedVersionsAsync(tx: null, ct)` (:177): if a baseline is
  set and `applied.Count == 0`, call `Check.DecideAsync`. On `MarkApplied`, open one transaction and insert a history row
  for every discovered migration with `Version <= ThroughVersion`, through `_dialect.InsertAppliedVersionSql` and the
  same parameters `ApplyOneAsync` binds (:366-375). Commit, then re-read `applied`. On failure, roll back with
  `AttachRollbackFailure` as `ApplyOneAsync` does. Update the class XML-doc algorithm list.
- [ ] **Step 5: Run the tests; expect PASS.** Then run the whole `Migrations` folder:
  `--filter "FullyQualifiedName~ZeroAlloc.ORM.Integration.Tests.Migrations"`. Expect no regressions.
- [ ] **Step 6: Add the PublicAPI entries and build `src/ZeroAlloc.ORM` with zero warnings.** Commit as
  `feat: add a baseline option to MigrationRunner`.

### Task 2: `EfCoreBaseline` check, Postgres coverage, docs

**Files:**
- Create: `src/ZeroAlloc.ORM/Migrations/EfCoreBaseline.cs`
- Modify: `src/ZeroAlloc.ORM/PublicAPI.Unshipped.txt`
- Test: `tests/ZeroAlloc.ORM.Integration.Tests/Migrations/EfCoreBaselineSqliteTests.cs`
- Test: `tests/ZeroAlloc.ORM.Integration.Tests/Postgres/PostgresEfCoreBaselineTests.cs` (`[Trait("Provider","Postgres")]`)
- Test: `tests/ZeroAlloc.ORM.Integration.Tests/SqlServer/SqlServerEfCoreBaselineSqlTests.cs`, a SQL-text test in the
  style of `SqlServerMigrationDialectTests`
- Modify: `docs/cookbook/migrations.md` (new section "Adopting an EF Core database", before "Custom dialects" :509)

**Interfaces:**
- Consumes: Task 1's `IMigrationBaselineCheck`, `BaselineDecision`, `MigrationBaseline`.
- Produces: `public static class EfCoreBaseline` with
  `public static IMigrationBaselineCheck LatestMigrationIs(string migrationId, SqlDialect dialect)`.
  It supports `SqlDialect.PostgreSql`, `SqlDialect.Sqlite` and `SqlDialect.SqlServer`; any other value throws
  `ArgumentOutOfRangeException`, and a null or whitespace id throws `ArgumentException`.
  Decision rules:
  - `__EFMigrationsHistory` absent → `RunAll`.
  - Present and `MAX("MigrationId")` equals `migrationId` (ordinal) → `MarkApplied`.
  - Present and empty, or a different latest id → throw `ZeroAllocOrmMigrationConflictException`. The message names the
    expected id and the found id (or "no rows"), and says to bring the database to the expected id with EF before
    switching.
  SQL per dialect, as `internal const string`s:
  - Postgres existence: `SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory')`
  - Sqlite existence: `SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'`
  - SQL Server existence: `SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '__EFMigrationsHistory'`
  - Latest id: `SELECT MAX("MigrationId") FROM "__EFMigrationsHistory"`, with `[MigrationId]` / `[__EFMigrationsHistory]`
    on SQL Server.

- [ ] **Step 1: Write the failing Sqlite tests.** In each, create `__EFMigrationsHistory ("MigrationId" TEXT PRIMARY KEY,
  "ProductVersion" TEXT NOT NULL)` by hand.
  - `No_ef_history_returns_RunAll`
  - `Latest_id_matches_returns_MarkApplied`: rows `20260209134036_AddMissingTaskColumns` and
    `20261014090300_AddNodeSelectionUniqueIndex`; expected = the second.
  - `Older_head_throws_naming_both_ids`: expected = second, only the first row present. The message contains both ids.
  - `Newer_head_throws`: expected = first, both rows present.
  - `Empty_ef_history_throws`.
  - `End_to_end_existing_db_marks_baseline_and_applies_later`: an EF history at the expected id, tables 1..2 present,
    source 1..3, `new MigrationRunner(conn, src, new SqliteMigrationDialect(), new MigrationBaseline(2, EfCoreBaseline.LatestMigrationIs(id, SqlDialect.Sqlite)))`.
    It applies only version 3, and `__EFMigrationsHistory` still exists with both rows.
- [ ] **Step 2: Run them; expect a compile failure.**
- [ ] **Step 3: Implement `EfCoreBaseline`.** Execute through the connection passed to `DecideAsync`, and read the scalar
  as `string`, or `DBNull` for an empty table.
- [ ] **Step 4: Run the Sqlite tests; expect PASS.**
- [ ] **Step 5: Write and run the Postgres tests.** In `PostgresEfCoreBaselineTests`:
  - `Existing_ef_database_is_adopted_in_place`: a seeded row in a baseline table survives, and only later migrations
    run.
  - `Fresh_database_runs_all`.
  - `Baseline_source_after_another_source_marks_only_its_own_rows`: run a source `other` with versions 1–2 first, then
    the baseline source `daedalus` (baseline 2, EF history at the expected id). The `other` rows are unchanged, the
    `daedalus` rows are 1,2 marked plus 3 applied.
  - `Pre_scoping_history_with_baseline_source_first_throws`: build the unscoped table as
    `MigrationSourceScopeTests` does, run the baseline source first, and expect the existing
    `ZeroAllocOrmMigrationConflictException`. This pins the documented ordering rule.

  Run: `--filter "FullyQualifiedName~PostgresEfCoreBaselineTests"`. Expect PASS.
- [ ] **Step 6: Write the SQL Server SQL-text test, `LatestMigrationIs_uses_bracketed_identifiers`,** and run it.
- [ ] **Step 7: Write the cookbook section.** Cover:
  - when to use it;
  - writing migration `N` as the schema at the last EF migration, e.g. from `dotnet ef migrations script`;
  - freezing EF after `N`;
  - bringing older databases to that id with EF first;
  - running other sources before a baseline source on a pre-scoping history table;
  - that `__EFMigrationsHistory` is left in place.

  Include a Daedalus-shaped example.
- [ ] **Step 8: Add the PublicAPI entries, build, and commit** as `feat: add an EF Core history check for migration baselines`.
- [ ] **Step 9: Open the PR.** Its body needs a "Behaviour change" section saying there is none for existing
  constructors.

---

## PR 2 — `feat: bind PostgreSQL arrays as parameters and columns`

Branch: `feat/array-parameters` off `main`.

### Task 3: `PrimitiveArray` convention and ZAO082

**Files:**
- Modify: `src/ZeroAlloc.TypeConversions/ConventionKind.cs`: append `PrimitiveArray` after `ExplicitMaterialize`, never
  in the middle.
- Modify: `src/ZeroAlloc.TypeConversions/ConventionDiscovery.cs:37`: try `PrimitiveArray` first, before `Primitive`,
  but never for `byte[]`.
- Modify: `src/ZeroAlloc.TypeConversions/ConventionResult.cs`: add the element type, e.g. `ITypeSymbol? ElementType`,
  plus a shape enum or flag naming which collection shape was matched. Add them as init-only properties, never as new
  positional parameters: `ConventionResult` is a shipped positional record, so a new parameter would break its
  constructor and `Deconstruct`.
- Modify: `src/ZeroAlloc.TypeConversions/PublicAPI.Unshipped.txt`
- Modify: `src/ZeroAlloc.ORM.Generator/Diagnostics/DiagnosticDescriptors.cs`, `OrmGenerator.cs` `LookupDescriptor`,
  `AnalyzerReleases.Unshipped.md`
- Create: `docs/diagnostics/ZAO082.md`; modify `docs/diagnostics/README.md`
- Test: `tests/ZeroAlloc.TypeConversions.Tests/PrimitiveArrayDiscoveryTests.cs`
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Diagnostics/ZAO082Tests.cs`

**Interfaces:**
- Produces:
  - `ConventionKind.PrimitiveArray` for the shapes `T[]`, `List<T>` and `IReadOnlyList<T>`, where `T` ∈ {`string`,
    `int`, `long`, `Guid`}. Other element types stay `Unknown`, so ZAO041 still fires.
  - `DiagnosticDescriptors.ZAO082_ProviderTypeRequiresNpgsql`, an Error. Message format: `"Parameter or column '{0}'
    uses {1}, which needs Npgsql. Reference the Npgsql package, or bind it another way."`, with
    `{1}` ∈ {`"a PostgreSQL array"`, `"[Json]"`}.
  - An internal helper `static bool HasNpgsql(Compilation c) => c.GetTypeByMetadataName("Npgsql.NpgsqlParameter") is not null;`
    in the generator. Compute it once per compilation, flow it into the method model as an equatable `bool`, and keep
    incrementality: don't capture `Compilation` in the model.

- [ ] **Step 1: Write the failing discovery tests.** Each shape × element type gives `PrimitiveArray` with the right
  `ElementType`. `byte[]` stays `Primitive`. `decimal[]` and `List<CustomerRow>` stay `Unknown`.
- [ ] **Step 2: Write the failing ZAO082 tests.**
  - `Array_parameter_without_npgsql_reports_ZAO082`: plain harness, a `[Query]` with a `string[] tags` parameter.
  - `Array_parameter_with_npgsql_reports_nothing`: the same source plus an Npgsql stub in the test source:

    ```csharp
    namespace Npgsql { public class NpgsqlParameter { public NpgsqlTypes.NpgsqlDbType NpgsqlDbType { get; set; } } }
    namespace NpgsqlTypes { public enum NpgsqlDbType { Jsonb = 14 } }
    ```

    The stub is not sealed, so `p is Npgsql.NpgsqlParameter` compiles against `IDbDataParameter`. Put it in a shared
    `NpgsqlStub` constant in the test project, for Tasks 4 and 6.
  - `Bulk_insert_collection_keeps_its_meaning`: an existing `[Command(Kind = BulkInsert)]` source with
    `IReadOnlyList<Row>` produces no new diagnostic.
- [ ] **Step 3: Run both test projects, one at a time; expect FAIL.**
- [ ] **Step 4: Implement discovery, the descriptor, the lookup entry, the release row and the docs page.** ZAO082 fires
  for every `PrimitiveArray` parameter or column when `HasNpgsql` is false.
- [ ] **Step 5: Run `ZeroAlloc.TypeConversions.Tests`, then the whole `ZeroAlloc.ORM.Generator.Tests`.** Expect PASS, with
  all 129 existing snapshots unchanged and `Every_diagnostic_helpLinkUri_resolves_to_existing_docs_file` passing.
- [ ] **Step 6: Commit** as `feat: classify primitive arrays and report ZAO082 without Npgsql`.

### Task 4: Emit array binding and array reads

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs`, at the four binding sites (`EmitParameterBinding` :8090,
  `EmitParameterBindingWithIndent` :7565, `EmitBatchCommandParameterBinding(WithIndent)` :7801/:7504) and the column reader
  selection (`TryBuildFlatRowMaterialization` :2449-2465, `TryBuildCompositeColumnBinding`, multi-result :3417/:3594,
  scalar :1748)
- Modify: `src/ZeroAlloc.TypeConversions/PrimitiveCatalog.cs`: a reader method for arrays, `GetFieldValue<T[]>`
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Emit/ArrayParameterEmitTests.cs` (new snapshots)

**Interfaces:**
- Consumes: Task 3's `ConventionKind.PrimitiveArray`, `ElementType`, shape, and the `HasNpgsql` flag.
- Produces the emitted shapes:
  - **Binding:** `Value = (object?)x ?? DBNull.Value`. A `T[]` or `List<T>` value is passed as-is. Any other
    `IReadOnlyList<T>` is passed as `x as T[] ?? x as List<T> ?? (object)[.. x]`. Npgsql infers the array type, so
    leave `NpgsqlDbType` unset.
  - **Reads:** `__reader.GetFieldValue<T[]>(o)` for `T[]` and `IReadOnlyList<T>`; `new List<T>(__reader.GetFieldValue<T[]>(o))`
    for `List<T>`. A nullable column guards with `IsDBNull` the way the existing nullable primitives do.

- [ ] **Step 1: Write the failing snapshot tests**, all with `NpgsqlStub`:
  - `String_array_parameter_binds_value`
  - `List_of_guid_parameter_binds_value`
  - `ReadOnlyList_parameter_binds_with_fallback_copy`
  - `Row_with_string_array_column_reads_GetFieldValue`
  - `Row_with_list_column_reads_into_new_list`
  - `Nullable_array_column_guards_IsDBNull`
- [ ] **Step 2: Run them; expect FAIL** (ZAO041, or missing snapshots).
- [ ] **Step 3: Implement the emit at every binding and reader site.** Read each generated snapshot and check it
  compiles in the harness: the snapshot tests already fail on emitted compile errors.
- [ ] **Step 4: Run the whole generator test project; expect PASS**, with the old snapshots unchanged.
- [ ] **Step 5: Commit** as `feat: emit PostgreSQL array parameter binding and column reads`.

### Task 5: Postgres integration, slim data source, docs

**Files:**
- Create: `tests/ZeroAlloc.ORM.Integration.Tests/ArrayRepo.cs`, a `[Dialect(SqlDialect.PostgreSql)]` partial repository
  with `[Query]`/`[Command]` methods:
  - an insert with a `string[] tags`
  - `= ANY(@ids)` with a `List<Guid>`
  - a read into `TaggedRow(Guid Id, string[] Tags)`
  - a read into `TaggedListRow(Guid Id, List<string> Tags)`
  - a nullable column
  - an `IReadOnlyList<string>` parameter
- Modify: `tests/ZeroAlloc.ORM.Integration.Tests/PostgresFixture.cs`: add
  `public static Task<PostgresFixture> CreateSlimAndInitializeAsync()`, which opens its connection from
  `new NpgsqlSlimDataSourceBuilder(cs).EnableArrays().Build()`. The default stays the full `NpgsqlConnection`.
- Test: `tests/ZeroAlloc.ORM.Integration.Tests/Postgres/PostgresArrayTests.cs` (`[Trait("Provider","Postgres")]`)
- Modify: `docs/README.md` "Parameters and columns" (:98-104); remove "array parameters" from "Not supported yet" (:197).
  Keep table-valued parameters there.
- Modify: `docs/cookbook/provider-quirks.md` "PostgreSQL (Npgsql)" (:221): arrays, and `EnableArrays()` on the slim
  builder.

- [ ] **Step 1: Write the failing tests**, each run on both the full and the slim fixture through a `[Theory]` over the
  two factory methods:
  - `String_array_round_trips`
  - `Any_with_guid_list_filters_rows`
  - `List_column_materialises`
  - `Null_and_empty_arrays_round_trip`: `null` stores `NULL` and reads back `null`; `[]` stores `'{}'`, reads back an
    empty array, and `= ANY(@p)` with `[]` returns zero rows.
  - `ReadOnlyCollection_parameter_binds_as_array`: pass `new ReadOnlyCollection<string>(["a","b"])`.
- [ ] **Step 2: Run them; expect FAIL**, then implement anything missing. If a slim-fixture case fails with Npgsql's
  "EnableArrays" error, the fixture is wrong, not the generator.
  Run: `--filter "FullyQualifiedName~PostgresArrayTests"`
- [ ] **Step 3: Run the AOT smoke gate locally** (`tests/ZeroAlloc.ORM.AotSmoke` publish, as in its CI workflow) and
  expect zero warnings.
- [ ] **Step 4: Update the docs, commit** as `feat: cover PostgreSQL arrays end to end`, **and open the PR.**

---

## PR 3 — `feat: bind [Json] strings as jsonb`

Branch: `feat/json-parameters` off `main`, after PR 2 merges, so it can reuse ZAO082, `HasNpgsql` and `NpgsqlStub`.

### Task 6: `[Json]` marker, ZAO083, emit

**Files:**
- Create: `src/ZeroAlloc.ORM.Abstractions/JsonAttribute.cs`:
  `[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)] public sealed class JsonAttribute : Attribute;`.
  The XML doc says it means "PostgreSQL jsonb; the value is a JSON document held as a string".
- Modify: `src/ZeroAlloc.ORM.Abstractions/PublicAPI.Unshipped.txt`
- Modify: `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs`:
  - detect `"ZeroAlloc.ORM.JsonAttribute"` on method parameters, record/ctor parameters and composite members, the way
    `StoreAsStringAttributeFqn` is detected (`ConventionDiscovery.cs:55`, :157);
  - add a `bool IsJson` to `ParameterInfo` (`Model/MaterializationModel.cs:168`);
  - emit at all four binding sites plus `EmitCompositeParameterBinding` (:8214).
- Modify: diagnostics files as in Task 3; create `docs/diagnostics/ZAO083.md`.
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Emit/JsonParameterEmitTests.cs`,
  `tests/ZeroAlloc.ORM.Generator.Tests/Diagnostics/ZAO083Tests.cs`, and two new cases in `ZAO082Tests`.

**Interfaces:**
- Consumes: Task 3's ZAO082 with `{1}` = `"[Json]"`, `HasNpgsql`, and `NpgsqlStub`.
- Produces:
  - `ZeroAlloc.ORM.JsonAttribute`.
  - `DiagnosticDescriptors.ZAO083_JsonRequiresString`, an Error: `"'{0}' carries [Json] but is of type '{1}'. [Json] marks
    a JSON document held as a string; use string or string?."`
  - The emit, placed after `.Value =`:
    `if ({local} is global::Npgsql.NpgsqlParameter __np_{name}) __np_{name}.NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Jsonb;`
  - Reads are unchanged: a `[Json]` column reads with the existing string reader.

- [ ] **Step 1: Write the failing tests.**
  - `Json_parameter_sets_NpgsqlDbType_Jsonb` (snapshot)
  - `Json_composite_member_sets_NpgsqlDbType_Jsonb` (snapshot)
  - `Json_column_reads_as_string` (snapshot: the same as an unmarked string)
  - `Json_on_int_reports_ZAO083`
  - `Json_without_npgsql_reports_ZAO082`
  - `Unmarked_string_unchanged`: the existing string snapshot is untouched
- [ ] **Step 2: Run them; expect FAIL.**
- [ ] **Step 3: Implement the attribute, detection, `IsJson`, the emit, the diagnostics, the docs page and the PublicAPI
  entries.**
- [ ] **Step 4: Run the whole generator test project; expect PASS** with the old snapshots unchanged.
- [ ] **Step 5: Commit** as `feat: add [Json] for jsonb string parameters`.

### Task 7: Postgres integration and docs

**Files:**
- Create: `tests/ZeroAlloc.ORM.Integration.Tests/JsonRepo.cs`, a `[Dialect(SqlDialect.PostgreSql)]` repository against a
  `documents (id uuid primary key, body jsonb, note jsonb null)` table. Its insert binds `[Json] string body` and
  `[Json] string? note`, and its read goes into `DocumentRow(Guid Id, [Json] string Body, [Json] string? Note)`.
- Test: `tests/ZeroAlloc.ORM.Integration.Tests/Postgres/PostgresJsonTests.cs` (`[Trait("Provider","Postgres")]`), run
  on the full and slim fixtures.
- Test: `tests/ZeroAlloc.ORM.Integration.Tests/JsonSqliteTests.cs`: the same repository shape without the Postgres
  dialect, against a `TEXT` column. This proves the type test falls through and the value binds as a plain string.
- Modify: `docs/README.md` type list, and `docs/cookbook/provider-quirks.md` (jsonb, no `::jsonb` cast needed).

- [ ] **Step 1: Write the failing tests.**
  - `Json_round_trips_without_cast`: the SQL text contains no `::jsonb`, and `SELECT body->>'a'` returns the stored value.
  - `Null_json_stores_sql_null`: `note IS NULL` is true.
  - `Invalid_json_surfaces_postgres_error`: throws `PostgresException` with `SqlState == "22P02"`.
  - `Json_on_sqlite_binds_as_text`.
- [ ] **Step 2: Run them; expect PASS once Task 6 is in.** If the slim fixture rejects jsonb strings, add the Npgsql
  slim-builder call it names to `CreateSlimAndInitializeAsync` and to the provider-quirks page. Don't change the
  generator.
- [ ] **Step 3: Run the AOT smoke gate locally; expect zero warnings.**
- [ ] **Step 4: Update the docs, commit** as `feat: cover jsonb parameters end to end`, **and open the PR.**

---

## After merge

- Confirm each `feat:` appears in the release-please PR. Releases ship in the owner's next batched cycle.
- Tell the Daedalus session the released versions, and note the baseline's ordering rule for its `WorkflowEngineMigrator`.
