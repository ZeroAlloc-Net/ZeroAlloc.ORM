# ZeroAlloc.ORM — Working Backlog

Things to work on for the `ZeroAlloc-Net/ZeroAlloc.ORM` project. Refined as we go — add new items here whenever something new surfaces. Items get crossed out when they ship.

> **Authoritative design:** [`2026-05-30-zeroalloc-orm-v1-design.md`](2026-05-30-zeroalloc-orm-v1-design.md). Anything contradicting that doc gets re-checked against it before action.

---

## v0.1 + v0.2 implementation status (live)

Branch: `main` (local-only). Tasks shipped (commits in chronological order):

- Task 0.1 — global.json SDK pin (`5b73eb8`)
- Task 0.2 — Abstractions test project (`fb3c089`)
- Task 0.3 — Generator snapshot test rig (`9eded7b`)
- Task 0.4 — Integration test fixture (`ad5e79c`)
- Tasks 1.1-1.6 — Six attributes (commits `ba4c6f9`..`6235481`)
- Task 1.7 — Exception types (`c3cff0c`)
- Task 2.1 — OrmGenerator skeleton (`7e2db26`)
- Polyfill remediation — IsExternalInit on Abstractions (`4cb5551`)
- Task 2.2 — Attribute scan + stub emit (`6e66c0a`)
- EquatableArray remediation (`01d1ee7`)
- Reviewer-flagged remediation pass — ModuleInitializer/Materialize/Debug.Assert/repository hoisting/plan-doc corrections (`2e9a676`..`6a3aef8`)
- Task 2.3 — IAsyncDbConnection resolution (primary ctor / field / property) (`fd36890`..`bf78073`)
- Phase 3 — Diagnostic catalog ZAO001-ZAO009 (`a6b5340`..`ff4072a`)
- Phase 3 polish — diagnostic plumbing (`023600e`, `9aee32c`)
- Task 4.1 — Scalar `Task<int>` emit + integration smoke + this.-prefix fix (`d5aaad1`, `996d6e8`, `7007cc0`, `7541566`)
- Task 4.2 — Compile-smoke harness (`406f4be`)
- Task 4.3 — Nullable scalar `Task<T?>` emit + snapshot (`df6a024`, `30db555`)
- Task 5.1 — FlatRow positional-record materialization (`1074cc4`, `d824781`)
- Task 5.1 fix — preserve parameter order + CT name forwarding (`8f34d53`)
- Task 5.2 — FlatRow integration smoke (`73c5b77`)
- Task 6.1 — Primitive parameter binding (int/string/decimal/Guid/DateTime/...) (`04a59dc`)
- Task 6.1.5 — `[Param(Name)]` SQL-side override (`c276914`)
- Task 6.2 — Nullable primitive parameter binding with DBNull guard (`ca457a8`)
- Task 6.3 — Primitive parameter integration round-trip suite (`c0ac5b9`)
- Task 6.4 — Keyword-named parameter `@`-prefix in emit (`e2aa3d4`)
- Task 6.5 — Compile-smoke coverage for `[Param(Name)]` + nullable param (`6acebbf`)
- Task 6.6 — Extended PrimitiveCatalog: DateTimeOffset, TimeSpan, byte[] (`855e4da`)
- Task 6.7 — Keyword-named CancellationToken `@`-prefix in emit (`619f55a`)
- Task 7.1 — AOT smoke test consumer + CI gate activation (`1fdeedc`)

### Post-bootstrap remediations (R1–R11, per [`2026-05-31-forward-plan.md`](2026-05-31-forward-plan.md))

- R1 — Trim public API surface to v0.1 + add ZAO020/ZAO021 info diagnostics (`2dc6025`, `9ac54a6` / `44b42db`)
- R2 — Drop ORM.Analyzers from v0.1; hoist PrimitiveCatalog into TypeConversions (`7bfd7f2`)
- R3 — NuGet publish wiring + NU5046/NU5128 pack fixes + icon (`3dcf8c3`)
- R4 — Connection-lifecycle integration test (`62fa1e5`)
- R5 — Diagnostic catalog docs (`62fa1e5`)
- R6 — Codegen polish bundle: IsTrimmable, [GeneratedCode], EquatableArray, ZAO022, docs (`2bc4d8a`)
- Drop -preview chore: first release tags v0.1.0 (`60161c3`)
- R7 — Diagnostic UX polish: ZAO007 message split, ZAO008 literal-aware, ZAO003/004 dedupe (`de0b656`)
- R8 — Type-scoped diagnostic hoist from QueryMethodModel to QueryRepositoryModel (`d89e284`)
- R9 — README Quick Start + Abstractions row drift fix (`517c45b`)
- R10 — Test infra hardening: curated reference list + FlatRow nullable round-trip + naming sweep (`dd25bbd`)
- R11 — Exception ctor symmetry + Roslyn pin rationale + backlog reconciliation (this commit)

**v0.1 milestone complete. Ready for release-please bump to `0.1.0` (R12).**

### v0.2 — value-objects + enums + domain entities (post-v0.1.0 release)

Commits in chronological order, all merged via PR on `main` after `v0.1.0` shipped:

- Phase A — v0.2 implementation plan (`72e110a`)
- Phase A.1 — re-add `[StoreAsString]` attribute to Abstractions (`292dcb1`)
- Phase B — `ConventionDiscovery` API build-out in TypeConversions (`f02359d`)
- Phase C — value-object materialization + binding (Phase C.1-C.5) (`4f99275`)
- Phase D — enum support: default-int round-trip + `[StoreAsString]` (D.1-D.2) (`a211b25`)
- Phase E + F.1 — DomainEntity emit shape + ZAO040 diagnostic (`1030862`)
- Phase F.2-F.5 — ZAO041-044 materialization diagnostics (`c6b14fd`)
- Phase G — integration round-trip coverage + README + release-please reset + this entry (this PR)

v0.2 milestone scoreboard:

- ~~v0.2-T1 — ZA.ValueObjects integration~~ — ✅ shipped 0.2.0
- ~~v0.2-T2 — Single-arg-ctor record discovery~~ — ✅ shipped 0.2.0
- ~~v0.2-T3 — Static factory discovery~~ — ✅ shipped 0.2.0
- ~~v0.2-T4 — Enum default int round-trip~~ — ✅ shipped 0.2.0
- ~~v0.2-T5 — `[StoreAsString]` attribute~~ — ✅ shipped 0.2.0
- ~~v0.2-T6 — Multi-arg domain entity materialization~~ — ✅ shipped 0.2.0
- ~~v0.2-T7 — Diagnostics ZAO040-ZAO044~~ — ✅ shipped 0.2.0

**v0.2 milestone complete. Release-please will propose 0.2.0 from conventional commits.**

---

## P0 — Bootstrap

### ORM-B1 — Create the `ZeroAlloc-Net/ZeroAlloc.ORM` repo

- User-owned action — needs org permissions.
- Initial skeleton: `Directory.Build.props`, `GitVersion.yml`, `release-please-config.json`, `ZeroAlloc.ORM.slnx`, empty src/tests folders, README placeholder.
- Workflows port over from AdoNet.Async: `ci.yml`, `aot-smoke.yml`, `release-please.yml`.
- New: `collision-smoke.yml` (ZA.Rest + ZA.ORM AOT publish, gates v1.0).
- NuGet API key + release-please org permissions configured.

### ORM-B2 — Commit the design doc into the new repo

- Copy `docs/plans/2026-05-30-zeroalloc-orm-v1-design.md` from `ZeroAlloc.Templates` to `docs/design/2026-05-30-v1.0-design.md` in the new repo.
- Keep the `ZeroAlloc.Templates` copy as the source until v1.0 ships, then archive there with a pointer to the canonical location.

### ORM-B3 — Initial project skeleton (no functionality yet)

Five csproj scaffolds with correct dependencies + AOT declarations. No source code beyond placeholder `// TODO` markers per package.

- `src/ZeroAlloc.ORM.Abstractions/` — `<IsAotCompatible>true</IsAotCompatible>`, netstandard2.0+net10.0 multi-target.
- `src/ZeroAlloc.ORM/` — `<IsAotCompatible>true</IsAotCompatible>`, net10.0, PackageReference to `AdoNet.Async` `[1.*]`.
- `src/ZeroAlloc.ORM.Generator/` — Roslyn incremental generator csproj template, netstandard2.0.
- `src/ZeroAlloc.TypeConversions/` — separate package, netstandard2.0, no `.ORM` prefix in the package name.
- `src/ZeroAlloc.ORM.Analyzers/` — analyzer csproj template.

Verify all five pack to NuGet correctly via the `dotnet pack` step.

---

## P0 — Milestone v0.1 (4 weeks)

Foundational generator + smoke test path. Everything in this milestone unblocks the next.

### ~~v0.1-T1 — Roslyn incremental generator skeleton~~ — ✅ shipped (Phase 2)

- `IIncrementalGenerator` implementation reading source syntax.
- Forward-pipeline structure: collect `[Query]`-annotated methods → group by containing type → emit per-type partial file.
- Output discipline per Section 4: deterministic emit, `[GeneratedCodeAttribute]`, `#nullable enable`.
- File naming: `<ContainingType>.g.cs` in obj output.

### ~~v0.1-T2 — `[Query]`, `[Param]` attribute definitions in Abstractions~~ — ✅ shipped (Phase 1)

- Exact shape from Section 2 of design doc.
- `MaterializeStrategy`, `BatchMode`, `CommandKind` enums included (some unused in v0.1 but lock the surface for later milestones).
- XML doc comments per public member referencing diagnostics.

### ~~v0.1-T3 — Method signature validation (ZAO001-ZAO009)~~ — ✅ shipped (Phase 3)

Compile-time diagnostics for the method signature contract:

- ZAO001 not `partial`.
- ZAO002 bad return type.
- ZAO003 no `IAsyncDbConnection` resolvable.
- ZAO004 containing type not `partial`.
- ZAO005 multiple annotation attributes.
- ZAO006 multiple CancellationToken.
- ZAO007 `IAsyncEnumerable<T>` without `[EnumeratorCancellation]`.
- ZAO008 `;` in SQL with single-result return type.
- ZAO009 redundant `async` keyword.

Each emits with a stable `id` + `helpLinkUri` (stubbed to GitHub Markdown file until docs site exists).

### ~~v0.1-T4 — Single-result `Task<T>` / `Task<T?>` emit~~ — ✅ shipped (Phase 4)

- Generator emits: `OpenAsync` (if not open), `CreateCommand`, parameter binding loop, `ExecuteReaderAsync`, single `ReadAsync`, materialization, `CloseAsync` (if we opened).
- Connection-lifecycle matches the EF-style ref-counted pattern (do NOT hold the slot longer than the command — Lesson learned from PR #145 investigation: za-clean's `OpenAsync` at method entry was the dominant cost driver).

### ~~v0.1-T5 — FlatRow materialization on positional records~~ — ✅ shipped (Phase 5)

- Detect `record T(p1, p2, ...)` with all-positional ctor.
- Emit `new T(reader.GetXxx(0), reader.GetXxx(1), ...)` matching ctor parameter order to column order.
- Handle null: `reader.IsDBNull(N) ? null : reader.GetXxx(N)` for nullable parameters.

### ~~v0.1-T6 — Primitive parameter binding (no value-objects yet)~~ — ✅ shipped (Phase 6)

- Supported in v0.1: `int`, `long`, `short`, `byte`, `bool`, `decimal`, `double`, `float`, `string`, `Guid`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `byte[]` (+ nullable variants).
- Value-object discovery deferred to v0.2.

### ~~v0.1-T7 — Snapshot test rig (Verify.NET)~~ — ✅ shipped (Phase 0/4/5)

- `tests/ZeroAlloc.ORM.Generator.Tests/` with Verify.NET setup.
- Initial snapshots: one per emit-shape variant (single-result `Task<T>`, single-result `Task<T?>`, scalar return, primitive parameter, no parameters).
- Verify.NET diff-on-PR pattern.

### ~~v0.1-T8 — AOT smoke test (mandatory CI gate)~~ — ✅ shipped (Phase 7)

- Mirror AdoNet.Async's pattern. `tests/ZeroAlloc.ORM.AotSmoke/` consumer using `[Query]` end-to-end against Sqlite in-memory.
- `.github/workflows/aot-smoke.yml` publishes linux-x64 with `PublishAot=true`, runs the resulting binary.
- Fail on any IL2026/IL2046/IL3050.

### ~~v0.1-T9 — Integration test rig~~ — ✅ shipped (Phase 0/4/5/6)

- `tests/ZeroAlloc.ORM.Integration.Tests/` with Sqlite in-memory default backend.
- Three smoke scenarios: read one row, read zero rows (null return), parameter type round-trips.
- Sets up xUnit fixture pattern; later milestones add more scenarios.

### v0.1-T10 — `0.1.0` NuGet release

- release-please configured to bump to `0.1.0`.
- All 5 packages publish.
- README adds Quick Start section.

---

## P1 — Milestone v0.2 (2 weeks): value-objects + enums

### ~~v0.2-T1 — ZA.ValueObjects integration (shared TypeConversions)~~ — ✅ shipped 0.2.0

- Build out `ZeroAlloc.TypeConversions` package: `ConventionDiscovery.Resolve(INamedTypeSymbol)`.
- Detect `[ValueObject]` attribute from ZA.ValueObjects.
- Emit `OrderId.From(reader.GetInt32(ord))` for materialization, `p.Value = id.Value` for binding.

### ~~v0.2-T2 — Single-arg-ctor record discovery~~ — ✅ shipped 0.2.0

- `record OrderId(int Value)` shape (without ZA.ValueObjects attribute).
- Same emit shape as v0.2-T1 — different discovery path.

### ~~v0.2-T3 — Static factory discovery~~ — ✅ shipped 0.2.0

- `T From(TPrim)` or `T FromValue(TPrim)` static methods.
- Generator emits the factory call.

### ~~v0.2-T4 — Enum default int round-trip~~ — ✅ shipped 0.2.0

- `(OrderStatus)reader.GetInt32(ord)` for materialization.
- `p.Value = (int)status` for binding.

### ~~v0.2-T5 — `[StoreAsString]` attribute~~ — ✅ shipped 0.2.0

- Type-level attribute on enums.
- Switches emit to `Enum.Parse<OrderStatus>(reader.GetString(ord))` / `p.Value = status.ToString()`.

### ~~v0.2-T6 — Multi-arg domain entity materialization~~ — ✅ shipped 0.2.0

- `class T` with single public ctor whose params match column names.
- Column-name-to-ctor-param resolution via `reader.GetOrdinal("ParamName")`.

### ~~v0.2-T7 — Diagnostics ZAO040-ZAO044~~ — ✅ shipped 0.2.0

- ZAO040: no resolvable construction strategy.
- ZAO041: no resolvable unwrap strategy.
- ZAO042: `[StoreAsString]` on non-enum.
- ZAO043: `[Materialize(Factory)]` missing method.
- ZAO044: ambiguous discovery.

---

## P1 — Milestone v0.3 (2 weeks): multi-result + streaming

Commits in chronological order, all merged via PR on `main` after `v0.2.0` shipped.
Phase plan: [`docs/plans/2026-05-31-v0.3-implementation.md`](2026-05-31-v0.3-implementation.md).

- Phase A — SqlStatementSplitter hoist + BatchEmitStrategy resolver (#40)
- Phase B — MultiResultSet emit (Auto / Batch / Joined / Detection) (#41)
- Phase C — `IAsyncEnumerable<T>` streaming emit (#42)
- Phase D — ZAO032 / ZAO033 multi-result-set arity diagnostics (#43)
- Phase E + F.1, F.2 — MultiResultSet integration tests, cookbook recipes, README v0.3 section, backlog reconciliation (this PR)

v0.3 milestone scoreboard:

- ~~v0.3-T1 — `IAsyncDbBatch` emit path~~ — ✅ shipped 0.3.0 (#41)
  - Generator detects multi-statement SQL with tuple return.
  - Emits `if (connection.CanCreateBatch) { /* batch */ } else { /* ;-joined */ }`.
  - Both paths produce the same `(T1, List<T2>)` result.
- ~~v0.3-T2 — Tuple-of-result-sets dispatch~~ — ✅ shipped 0.3.0 (#41, integration coverage in this PR)
  - `Task<(OrderRow Head, List<OrderLineRow> Lines)?>` return type.
  - Each tuple field materializes from a separate result set via `NextResultAsync`.
- ~~v0.3-T3 — `IAsyncEnumerable<T>` streaming~~ — ✅ shipped 0.3.0 (#42)
  - Generator emits an `async IAsyncEnumerable<T>` body with `[EnumeratorCancellation]` flowing through.
  - Correct reader cleanup on early exit (yield broken by caller).
  - Diagnostic ZAO007 fires if `[EnumeratorCancellation]` missing.
- ~~v0.3-T4 — Multi-result-set diagnostics~~ — ✅ shipped 0.3.0 (#43)
  - ZAO032: tuple has more elements than `;`-statements.
  - ZAO033: tuple has fewer elements than `;`-statements.

**v0.3 milestone complete. Release-please will propose 0.3.0 from conventional commits.**

---

## Post-v0.3 cleanup

Items surfaced during the Phase C code review (2026-05-31, PR #42) that are intentionally
deferred rather than blocking the streaming PR. Pick up under v0.3 polish or roll into v0.4.

### ~~v0.3-CLN1 — Hoist `GetOrdinal` once per column in column-name materialization~~ — ✅ shipped in v0.6 Phase D (PR #71)

- Source: PR #42 code review (2026-05-31).
- Affected all column-name emit paths (DomainEntity in Streaming, FlatRow, plus the
  single-row DomainEntity path) — every emit previously produced both
  `__reader.IsDBNull(__reader.GetOrdinal("Col"))` and
  `__reader.GetXxx(__reader.GetOrdinal("Col"))`, so `GetOrdinal` ran twice per row per
  column in the hot loop.
- v0.6 Phase D fix: emit now produces `var __o_Col = __reader.GetOrdinal("Col");` once
  before the materialization body and reuses the local in both the `IsDBNull` and
  `GetXxx` calls across FlatRow / DomainEntity / Streaming-DomainEntity paths in a
  single pass.

### v0.3-CLN2 — Lift keeper-connection / shared-cache helper into SqliteFixture

- Source: PR #42 code review (2026-05-31), Fix 3 follow-up.
- `StreamingTests.Early_break_cleans_up_reader_and_closes_connection` uses a keeper
  + `Mode=Memory;Cache=Shared` pattern to keep the in-memory DB alive across the
  repo's open/close cycle. The pattern is private to that one test today.
- Fix: add `SqliteFixture.CreateSharedMemory(string name)` (or similar) once a second
  streaming-style early-close test lands — premature without the second adopter.
- A TODO marker is already in place at the top of `StreamingTests`.

### v0.4-CLN1 — Investigate single-pipeline architecture for `[Query]` + `[Command]` + `[StoredProcedure]`

- Source: PR #50 code review (2026-05-31, Phase A); extended by PR #53 code review (2026-05-31, Phase D).
- The v0.4 Phase A union step in `OrmGenerator.Initialize` joins the `[Query]` and
  `[Command]` `ForAttributeWithMetadataName` pipelines via `Collect()` + `SelectMany`.
  Convenient and locally simple, but the join collapses incremental-cache granularity:
  any source edit re-runs the union + grouping for ALL methods, not just the pipeline
  whose attribute saw the change.
- Phase D added a THIRD pipeline (`[StoredProcedure]`) and amplified the same loss.
- Investigate folding all three into a single `ForAttributeWithMetadataName` with a
  combined attribute filter, OR adopting a shared `CreateSyntaxProvider` that reads
  any of the three attributes in one pass. Either approach must preserve the per-method
  symbol-walking work the current `TransformMethod` does (no regressions on the larger
  per-method cost) — only the union step is the cache-invalidation hot spot.
- Bundled scope (PR #53 follow-up): collapse the in-line `isCommandAttribute` /
  `isStoredProcedureAttribute` convenience bools (`OrmGenerator.cs:191-201`) into
  pure `pipelineKind` switch usage at every call site. The bools were kept short-term
  for diff minimization during Phase D; the architecture pass is the right time to
  retire them.
- Defer to v0.4 polish after Phase D lands; chasing it during Phase A risks reshuffling
  emit semantics for an aesthetic gain.

### v0.4-CLN5 — Diagnose non-default `Batch` on `[StoredProcedure]`

- Source: PR #53 code review (2026-05-31, Phase D fix-up).
- `StoredProcedureAttribute.Batch` is accepted (kept for surface symmetry with
  `[Query]`) but currently ignored — sprocs always route through the joined/single-
  command path regardless of `BatchMode.Always` / `BatchMode.Auto`. Adopters writing
  `[StoredProcedure("usp_X", Batch = BatchMode.Always)]` get silent acceptance and
  no behavior change, which is materially confusing.
- Fix: add an Info-severity diagnostic (tentative ZAO063 — reserve a number in the
  next phase that touches the descriptor catalog) that fires when `Batch` is set
  to anything other than the `BatchMode.Never` default on a `[StoredProcedure]`
  method. Message: "Method '{0}' has [StoredProcedure(Batch = {1})] but Batch is
  ignored on stored procedures (they encapsulate their own batching semantics)."
- Defer to v0.4 polish or v0.5; not urgent enough to gate Phase D / Phase E.

### v0.4-CLN6 — Revisit per-element span granularity for named-tuple ZAO062

- Source: PR #55 review (2026-05-31, Phase F).
- ZAO062 (named-tuple output-parameter field doesn't match any method
  parameter) currently reports against a cache-safe `LocationInfo` derived
  from the method declaration rather than the per-element span of the
  offending tuple field. The cache-safe form was picked deliberately to
  keep the diagnostic in the incremental-generator hot path without
  pulling `SyntaxNode` references into the cached model.
- Trade-off: adopters see the diagnostic anchored on the method signature
  instead of the specific tuple field, which is slightly noisier when a
  big tuple has one bad name.
- Fix options to evaluate later: (1) lift just the tuple-element span into
  `LocationInfo` (per-element offset within the method declaration —
  cheap if we already walk the tuple syntax for Phase E emit); (2) keep
  the method-level location and add the offending field name in the
  message (lowest cost). Option 2 is already partially in place via the
  `{0}` placeholder in the diagnostic message.
- Defer to v0.4 polish or roll into the v0.6 diagnostics-polish milestone
  (`v0.6-T2` full catalog audit) where every ZAO code gets a per-trigger
  unit test pass anyway.

### ~~v0.3-CLN3 — `IAsyncDbConnection.CanCreateBatch` not forwarded for Sqlite~~ — ✅ resolved in v0.6 Phase A

- Source: PR #44 fix-up (2026-05-31).
- `Microsoft.Data.Sqlite` ≥9.x exposes `CanCreateBatch = true` on `SqliteConnection`,
  but `AdoNet.Async.Adapters.AsAsync()` wrapper reports `CanCreateBatch = false`.
- Consequence (pre-v0.6): `MultiResultSetTests` Auto + Never variants both
  exercised the `;`-joined fallback branch — no integration test in the Sqlite
  suite drove the `IAsyncDbBatch` path. Batch-branch emit was proven via the
  generator snapshot test `MultiResultSetAutoTests.Tuple_with_record_and_list_emits_runtime_CanCreateBatch_branch`.
- v0.6 Phase A resolution: Postgres via Npgsql properly forwards
  `CanCreateBatch == true` through the AdoNet.Async wrapper. The
  Postgres-backed `MultiResultSetTests` (under `tests/ZeroAlloc.ORM.Integration.Tests/Postgres/`)
  pins this with an explicit `fx.Connection.CanCreateBatch.Should().BeTrue()`
  assertion at the top of each Auto variant, and the round-trip succeeds —
  so the runtime IAsyncDbBatch branch finally has end-to-end coverage.
  Sqlite continues to drive the fallback branch (no behavioural change there).
  Whether the Sqlite-specific `AsAsync()` forwarding is fixed upstream remains
  open as an upstream-adapter polish item — but it no longer blocks ORM
  integration coverage.
- File-header comment in `tests/ZeroAlloc.ORM.Integration.Tests/MultiResultSetTests.cs`
  retains the original honesty about Sqlite's fallback behaviour for
  archaeology.

### ~~v0.3-CLN4 — Collapse release-please to a single linked version~~ — ✅ shipped

- Source: v0.3 release prep (2026-05-31, PR #46 fix-up).
- Picked **Option 2** (collapse to a single root config). Matches the dominant
  pattern across the ZA ecosystem: ZA.Rest, ZA.Validation, ZA.Telemetry, and
  ZA.ValueObjects all ship multiple NuGet packages from one root entry. The
  4 ORM packages are tightly coupled and always ship at the same SemVer, so
  per-package versioning was only creating drift.
- `release-please-config.json` reduced to a single `"."` block; manifest reduced
  to `{ ".": "0.3.0" }`; per-package CHANGELOGs removed (root CHANGELOG.md owns
  the history); `release-as: "0.3.0"` overrides removed.
- Next release (v0.4.0) computes from `v0.3.0` tag → all 4 NuGet packages will
  be packed at the unified version by `pack-push.yml`.

---

## P1 — Milestone v0.4 (2 weeks): commands + sprocs

Commits in chronological order, all merged via PR on `main` after `v0.3.0` shipped.
Phase plan: [`docs/plans/2026-05-31-v0.4-implementation.md`](2026-05-31-v0.4-implementation.md).

- Phase A — `[Command]` foundation + NonQuery emit (#50)
- Phase B — `[Command]` Scalar emit (#51)
- Phase C — `[Command]` Identity emit (provider-aware suffixes; Sqlite end-to-end, MSSQL/Postgres prepared) (#52)
- Phase D — `[StoredProcedure]` basic + multi-result-set + early ZAO061 (empty procedure name) (#53)
- Phase E — Named-tuple output parameters on `[StoredProcedure]` (#54)
- Phase F — Final diagnostics: ZAO060 (reserved) + ZAO062 (named-tuple field has no matching parameter) + ZAO005 verification (#55)
- Phase G — Cookbook recipes (`commands.md` + `stored-procedures.md`) (#56)
- Phase H — README v0.4 section + backlog reconciliation (this PR)

Test-count delta: 245 → 250 passing + 1 skipped placeholder (stored-procedure integration round-trip — Sqlite has no sproc support, deferred to v0.6 Postgres fixture).

v0.4 milestone scoreboard:

- ~~v0.4-T1 — `[Command]` attribute + emit~~ — ✅ shipped 0.4.0 (#50, #51, #52)
  - `Kind = NonQuery` → returns `int` rows-affected.
  - `Kind = Scalar` → returns first-column-first-row through the standard materialization pipeline (primitive / value-object / enum / single-arg-ctor wrappers).
  - `Kind = Identity` → provider-aware suffixes. Sqlite path drives `LAST_INSERT_ROWID()` end-to-end; the SQL Server (`SCOPE_IDENTITY()`) and Postgres (`RETURNING`) routing remains a v2 provider-routing follow-up — emit ships the suffix shape but adopters on those providers should verify against their dialect until the routing test fixtures land.
- ~~v0.4-T2 — `[StoredProcedure]` attribute + emit~~ — ✅ shipped 0.4.0 (#53)
  - `CommandType = StoredProcedure` on the emitted command, procedure name as `CommandText`.
  - Result shapes mirror `[Query]`: scalar, single-row, list, and multi-result-set tuples (head + lines through `NextResultAsync`).
  - Parameters bind by name, same convention as `[Query]`.
- ~~v0.4-T3 — Named-tuple output parameters~~ — ✅ shipped 0.4.0 (#54)
  - Tuple-return fields beyond the first map to `ParameterDirection.Output` SQL parameters by name.
  - Output values are copied back into the returned tuple after execution.
  - First tuple field remains the result-set materialization (scalar / row / list).
- ~~v0.4-T4 — Sproc diagnostics~~ — ✅ shipped 0.4.0 (#53, #55)
  - ZAO060 — reserved. The C# compiler already rejects `out`/`ref` on async-returning partials, so a dedicated source-generator diagnostic isn't needed at the emit layer right now; the code stays reserved against a future sync sproc shape (or a kept-for-symmetry future expansion).
  - ZAO061 — empty procedure name on `[StoredProcedure("")]`. Shipped early in Phase D fix-up (#53) when the empty-name case surfaced during sproc emit testing, rather than waiting for Phase F.
  - ZAO062 — named-tuple output-parameter field doesn't match any method parameter that could carry the output back to the SQL side. Shipped with Heuristic 1 in Phase F (#55) — the cache-safe `LocationInfo` form was used for the diagnostic location (see v0.4-CLN6).
  - ZAO005 — multi-attribute-on-one-method verification (`[Query]` + `[Command]` + `[StoredProcedure]` are mutually exclusive). Existing diagnostic confirmed firing on the new attributes in Phase F.

**v0.4 milestone complete. Release-please will propose 0.4.0 from conventional commits.**

---

## P1 — Milestone v0.5 (1 week): composites + custom factories

Commits in chronological order, all merged via PR on `main` after `v0.4.0` shipped.
Phase plan: [`docs/plans/2026-05-31-v0.5-implementation.md`](2026-05-31-v0.5-implementation.md).

- Phase A — composite materialization (Money pattern; MultiArgCtor classifier + emit at scalar, FlatRow, DomainEntity nested) (#60)
- Phase B — composite parameter binding (positional unpacking `@total_Amount` + `@total_Currency`) + early ZAO063 (sproc-batch ignored info) (#61)
- Phase C — nullable composite handling (all-or-nothing semantics) + ZAO050 (#62)
- Phase D — `[Materialize(Factory = "...")]` resolution + ZAO043 / ZAO044 / ZAO051 (#63)
- Phase E — ZAO052 (recursive composite deferred) + composites cookbook + composite × value-object snapshots (#64)
- Phase F — README v0.5 section + backlog reconciliation (this PR)

Test-count delta: 310 → 340 passing + 1 skipped (the v0.4 sproc-integration placeholder; Sqlite has no sproc support — deferred to v0.6 Postgres fixture).

v0.5 milestone scoreboard:

- ~~v0.5-T1 — Multi-column composite materialization~~ — ✅ shipped 0.5.0 (#60)
  - `Money(decimal Amount, string Currency)` — N columns per composite.
  - Generator tracks expanded column count beyond C# ctor arity.
  - Nested in flat rows: `record OrderRow(int Id, Money Total)` → 3 columns.
  - Composite ctor parameters resolve through the existing convention pipeline
    (primitive / enum / value-object / single-arg-ctor / static-factory).
- ~~v0.5-T2 — Multi-column composite binding~~ — ✅ shipped 0.5.0 (#61)
  - Method parameter typed `Money total` → SQL params `@total_Amount` + `@total_Currency`.
  - Naming convention via positional unpacking (parameter name + `_` + ctor-parameter name).
- ~~v0.5-T3 — `[Materialize(Factory = "X")]` resolution~~ — ✅ shipped 0.5.0 (#63)
  - Explicit `static` factory lookup by name on the type, wins over MultiArgCtor convention.
  - Factory parameter list maps to columns by name; positional fallback when SQL column
    names aren't available (see v0.5-CLN4).
  - Diagnostics: ZAO043 (factory not found), ZAO044 (ambiguous discovery — already
    shipped in v0.2, verified firing on the new factory path), ZAO051 (factory
    parameter list cannot be reconciled with available columns).
- ~~v0.5-T4 — Nullable composite handling~~ — ✅ shipped 0.5.0 (#62)
  - All composite columns `DBNull` → `null`.
  - Any-but-not-all `DBNull` → `ZeroAllocOrmMaterializationException` at runtime.
  - Compile-time warning ZAO050 fires on each nullable-composite materialization site
    to flag the partial-null case as undetectable at compile time (by design).

Phase E's cookbook + ZAO052 cleanup pass is not its own T item per the original backlog
banding — it's the final scope-guard + adopter-docs sweep for the milestone.

**v0.5 milestone complete. Release-please will propose 0.5.0 from conventional commits.**

---

## Post-v0.5 cleanup

Items surfaced during the v0.5 milestone that are intentionally deferred rather than
blocking the release PRs. Pick up under v0.5 polish or roll into v0.6.

### v0.5-CLN1 — ZAO050 per-position firing (Heuristic 1 refinement)

- Source: v0.5 Phase C review (2026-05-31, PR #62).
- ZAO050 (nullable composite — partial-null undetectable at compile time) currently fires
  once per nullable-composite materialization SITE rather than once per nullable-composite
  POSITION within a composite materialization (e.g. a row containing two `Money?` fields
  gets one warning at the method, not one warning per field).
- This is the same trade-off ZAO062 originally had before its tightening pass — the
  site-level firing was picked for cache-safe `LocationInfo` and to keep the diagnostic
  out of the per-position hot loop in the incremental generator.
- Fix options to evaluate later: (1) lift a per-position offset into `LocationInfo`
  (analogous to the ZAO062 tightening tracked in v0.4-CLN6); (2) keep the site-level
  location and include the offending field name(s) in the message text.
- Defer to v0.6 diagnostics polish (`v0.6-T2` full-catalog audit covers this anyway).

### v0.5-CLN2 — Nullable reference-type composite parameter binding

- Source: v0.5 Phase C scoping decision (2026-05-31, PR #62).
- A nullable composite REFERENCE TYPE used as a method parameter (e.g. `Money? total`
  where `Money` is a `class` / `record class`, not a struct) currently routes through
  the existing ZAO041 fallback (no resolvable unwrap strategy) rather than emitting
  the `if (total is null) bind DBNull else unpack` shape that the Phase C plan
  described as the natural symmetric path.
- Decision: Phase C committed to **Option A** — keep the existing ZAO041 surface and
  document the gap as a v0.5 follow-up. Adopters wanting nullable-composite binding
  today should reach for an explicit overload or pass the composite through a wrapper.
- Fix: extend `EmitParameterBindingWithIndent` (and the composite unpacking helper)
  to recognise `Nullable<T>` of a value-type composite directly (already partly
  handled) AND a reference-type composite null-check ahead of the unpack — emit one
  DBNull bind per inner column on the null branch.
- Defer to v0.6 or v0.5.1 — gated by real adopter demand. The struct-composite case
  (which v0.5 ships) covers the canonical `Money` / `Address` shape.

### v0.5-CLN3 — Recursive composite support (deferred via ZAO052)

- Source: v0.5 Phase A scope guard, hardened to a diagnostic in Phase E (PR #64).
- Today a composite ctor parameter whose type is itself a composite (`record
  OrderLine(Money UnitPrice, Money Tax)` with `Money(decimal Amount, string Currency)`)
  fires ZAO052 with a clear "recursive composites deferred to v0.6+" message rather
  than silently emitting a wrong column-index walk.
- Fix: extend the classifier + the column-index math to handle arbitrary nesting
  depth (the FlatRow path already does this for 1 level — generalising it is a
  straight bookkeeping change). Worth a dedicated phase since the snapshot churn
  will touch every emit-shape variant.
- Defer to v0.6 — paired with the Postgres / SQL Server integration fixture work
  so the deeper integration coverage doubles as a regression net.

### v0.5-CLN4 — Factory parameter-to-column name matching falls back to positional

- Source: v0.5 Phase D review (2026-05-31, PR #63).
- `[Materialize(Factory = "FromText")]` currently maps the factory's parameter list to
  columns by NAME when SQL column names are known at compile time (e.g. an inline
  `SELECT Amount, Currency FROM ...`) and falls back to POSITIONAL mapping otherwise
  (e.g. `SELECT * FROM ...`, or a stored-procedure result set where the generator
  cannot statically introspect the column list).
- Trade-off: positional fallback is the safe default but loses the "factory parameter
  named `Amount` maps to column named `Amount` no matter where it sits in the SELECT"
  ergonomic. Adopters writing factories whose parameter order doesn't match column
  order get surprising results today on the fallback path.
- Fix: improve when SQL parsing / column-introspection lands (already gated on
  ORM-V2-3 — SQL-parser-based analyzer). Pre-v1.0, document the fallback explicitly
  in `docs/cookbook/composites.md` (the recipe already calls this out for the
  Sqlite-decimal-as-text case).
- Defer to v0.6 or later — bundle with ORM-V2-3 when that lands.

---

## P1 — Milestone v0.6 (5-6 days): Postgres fixture + diagnostics polish + observability composition recipe

Commits in chronological order, all merged via PR on `main` after `v0.5.0` shipped.
Phase plan: [`docs/plans/2026-06-01-v0.6-implementation.md`](2026-06-01-v0.6-implementation.md).

- Phase A — Postgres integration fixture via Testcontainers; ports FlatRow / multi-result-set / streaming / sproc / `[Materialize(Factory)]` round-trips to a real Postgres backend (#68)
- Phase B — diagnostics catalog audit: backfill missing reference page (ZAO022); add `DiagnosticHelpLinkTests`; positive/negative coverage for ZAO001 and ZAO043; README catalog table (#69)
- Phase C — ZA.Telemetry observability cookbook recipe at `docs/cookbook/observability.md`; collision smoke deferred to v0.6-CLN1 due to upstream nullable-annotation issues (#70)
- Phase D — backlog cleanup batch: v0.3-CLN1 (GetOrdinal hoist) + v0.5-CLN5 (PR-title lint workflow) (#71)
- Phase E — README v0.6 section + backlog reconciliation (this PR)

Test-count delta: 340 → 368 passing + 1 skipped (+28 net). The +28 comes primarily
from the Postgres integration suite (FlatRow / multi-result-set / streaming / sproc /
factory / composites) plus diagnostic positive/negative pairs. The 1 skipped is the
original v0.4 sproc placeholder retained as archaeology; the Postgres sproc suite
provides the real runtime coverage.

v0.6 milestone scoreboard:

- ~~v0.6-T2 — Full diagnostics catalog audit~~ — ✅ shipped 0.6.0 (#69)
  - All shipping ZAO codes (ZAO001-ZAO063 inclusive of reserved ZAO060) verified
    via `DiagnosticHelpLinkTests` — every `DiagnosticDescriptor.HelpLinkUri`
    resolves to a real, non-empty markdown file under `docs/diagnostics/`.
  - Positive + negative coverage backfilled for ZAO001 and ZAO043; remaining
    descriptors already had coverage from prior milestones.
- ~~v0.6-T4 — `docs/diagnostics/` reference pages~~ — ✅ shipped 0.6.0 (#69)
  - ZAO022 backfilled (missing prior to v0.6). All other codes already had pages
    from earlier milestones; the audit confirmed completeness.
  - README diagnostics catalog table links each code to its docs page.
- ~~v0.6-T5 — ZA.Telemetry composition cookbook recipe~~ — ✅ shipped 0.6.0 (#70)
  - `docs/cookbook/observability.md` documents the consumer-seam composition pattern.
  - ZA.ORM ships **no** built-in `ActivitySource`; observability lives at the adopter
    boundary via `[Instrument]` (ZA.Telemetry) on a `partial class` that also carries
    `[Query]` (ZA.ORM). The two generators emit independently.
  - Collision smoke test (`ZeroAlloc.ORM.TelemetryCollision.AotSmoke`) was attempted
    but backed out — see v0.6-CLN1 for the deferral reason.

**v0.6 milestone complete. Release-please will propose 0.6.0 from conventional commits.**

---

## Post-v0.6 cleanup

Items surfaced during the v0.6 milestone that are intentionally deferred rather than
blocking the release PRs. Pick up under v0.6 polish or roll into v0.7.

### v0.6-CLN1 — Re-attempt ZA.Telemetry collision smoke test

- Source: v0.6 Phase C (2026-06-01).
- Attempted to ship a `tests/ZeroAlloc.ORM.TelemetryCollision.AotSmoke/` project
  composing `[Instrument]` (ZA.Telemetry) with `[Query]` (ZA.ORM) but the build
  hit nullable-annotation issues in ZA.Telemetry's InstrumentGenerator:
  - CS8613: wrapper method drops nullable annotation on `Task<T?>` return types.
  - CS8603: possible null reference return on the generated wrapper.
- Backed out the smoke project + workflow in v0.6 Phase C fix-up.
- Re-attempt once ZA.Telemetry ships a generator update that preserves
  nullable annotations across the `[Instrument]` boundary.
- Cookbook recipe at `docs/cookbook/observability.md` retains the
  conceptual composition pattern.

---

## P2 — Milestone v0.7 (1 week): benchmarks + collision + polish + API freeze

Commits in chronological order, all merged via PR on `main` after `v0.6.0` shipped.
Phase plan: [`docs/plans/2026-06-01-v0.7-implementation.md`](2026-06-01-v0.7-implementation.md) (#74).

- Phase A — BenchmarkDotNet suite: 4 workloads × 3 baselines (hand-written ADO.NET / Dapper.AOT / ZA.ORM) × 2 backends (Sqlite + Postgres) (#76)
- Phase B — ZA.Rest collision smoke test (`tests/ZeroAlloc.ORM.GeneratorCollision.AotSmoke/`) + `.github/workflows/collision-smoke.yml`; discovery: `QueryAttribute` name collision resolved via file-scoped `using` aliases. **The v1.0 release gate.** (#77)
- Phase C — README polish: packages table with AOT column, four-snippet Quick Start, dedicated NativeAOT compatibility section (#78)
- Phase D — API review + `Microsoft.CodeAnalysis.PublicApiAnalyzers` wired across `ZeroAlloc.ORM`, `ZeroAlloc.ORM.Abstractions`, `ZeroAlloc.TypeConversions`; **v1.0 surface lock at 103 entries / 16 public types** in `PublicAPI.Shipped.txt` (#81)
- Phase E — README v0.7 section + backlog reconciliation (this PR)

Test-count delta: 368 → 368 passing + 1 skipped (no net change). Phase A adds the
benchmark project but BDN harness isn't part of the unit-test count; Phase B adds an
AOT publish gate (`collision-smoke.yml`), not unit tests; Phase D adds analyzer-based
build-time guards rather than tests. The 1 skipped placeholder remains the v0.4 sproc
archaeology row (the Postgres sproc suite is the real runtime coverage).

v0.7 milestone scoreboard:

- ~~v0.7-T1 — Benchmark suite~~ — ✅ shipped 0.7.0 (#76)
  - `tests/ZeroAlloc.ORM.Benchmarks/` ships with BDN.
  - Comparisons: hand-written ADO.NET (baseline), Dapper.AOT, ZeroAlloc.ORM.
  - Workloads: single-row read, multi-row read, head + lines (multi-result), insert.
  - Backends: Sqlite in-memory and Postgres via Testcontainers.
  - Carry-forward: v0.7-CLN1 — re-capture real BDN numbers once SDK 10.0.300 is
    locally available; the artifacts under `docs/benchmarks/` are placeholders today.
- ~~v0.7-T2 — ZA.Rest collision smoke test~~ — ✅ shipped 0.7.0 (#77)
  - `tests/ZeroAlloc.ORM.GeneratorCollision.AotSmoke/` references both
    `ZeroAlloc.Rest.Generator` and `ZeroAlloc.ORM.Generator`, uses both attribute
    surfaces, AOT-publishes.
  - `.github/workflows/collision-smoke.yml` runs on every PR.
  - Discovery: both libraries ship a `QueryAttribute` — resolved at the consumer
    seam via file-scoped `using` aliases (`using ORMQuery = ZeroAlloc.ORM.Abstractions.QueryAttribute;`).
  - **The v1.0 release gate.** Breaking this workflow blocks the v1.0 cut.
- ~~v0.7-T3 — README + Quick Start polish~~ — ✅ shipped 0.7.0 (#78)
  - Packages table now carries an AOT compatibility column (every package ✅).
  - Quick Start covers four canonical shapes: single-row read, streaming, insert
    returning identity, stored procedure with output parameters.
  - Dedicated "NativeAOT compatibility" section calling out the AOT smoke +
    collision smoke CI gates and pointing at the benchmark suite for numbers.
- ~~v0.7-T4 — API review pass + surface lock~~ — ✅ shipped 0.7.0 (#81)
  - `Microsoft.CodeAnalysis.PublicApiAnalyzers` wired across `ZeroAlloc.ORM`,
    `ZeroAlloc.ORM.Abstractions`, `ZeroAlloc.TypeConversions`.
  - `PublicAPI.Shipped.txt` baselined at **103 entries across 16 public types**.
  - Any accidental addition / change / removal of a public member now breaks
    `dotnet build`. Additive v1.x evolution must go through `PublicAPI.Unshipped.txt`
    with explicit reviewer sign-off.
  - The surface lock holds until v1.0 ships.

**v0.7 milestone complete. Release-please will propose 0.7.0 from conventional commits.**

---

## Post-v0.7 cleanup

Items surfaced during the v0.7 milestone that are intentionally deferred rather than
blocking the release PRs. Pick up under v0.7 polish or roll into v1.0.

### v0.7-CLN1 — Capture real BenchmarkDotNet numbers once SDK 10.0.300 is locally available

- Source: v0.7 Phase A (PR #76).
- The BDN harness, project skeleton, workloads, baselines, and runner wiring all
  ship in v0.7.0. The artifacts under `docs/benchmarks/` are placeholder values
  pending a clean local run against the pinned SDK and pinned NuGet versions.
- Fix: re-run the suite locally once SDK 10.0.300 is available, regenerate the
  artifacts under `docs/benchmarks/`, and update the README's "Performance"
  pointer to call out the captured-date so adopters know the numbers are real.
- Defer to v1.0 release prep or as a standalone docs PR — gating v0.7.0 on
  perfect numbers would have stretched the release window past the target.

### Carry-forward items still open after v0.7

These items were originally tagged against earlier milestones and remain open
heading into v1.0 polish:

- **v0.3-CLN2** — Lift keeper-connection / shared-cache helper into SqliteFixture.
- **v0.4-CLN1** — Investigate single-pipeline architecture for `[Query]` + `[Command]` + `[StoredProcedure]`.
- **v0.4-CLN5** — Diagnose non-default `Batch` on `[StoredProcedure]` (informational diagnostic).
- **v0.4-CLN6** — Revisit per-element span granularity for named-tuple ZAO062.
- **v0.5-CLN1** — ZAO050 per-position firing refinement.
- **v0.5-CLN2** — Nullable reference-type composite parameter binding.
- **v0.5-CLN3** — Recursive composite support (ZAO052 still flags them today).
- **v0.5-CLN4** — Factory parameter-to-column matching falls back to positional.
- **v0.6-CLN1** — Re-attempt ZA.Telemetry collision smoke once upstream fixes nullable annotations.

Each item carries the original "fix options" and "defer to" notes from its
milestone-of-origin entry above. None block the v1.0 release gates — they are
quality-of-life polish that adopters will start feeling once v1.0 ships.

---

## P2 — v1.0 release gates

### v1.0-G1 — Cookbook docs

- `docs/cookbook/` with 7 recipes from design doc Section 5.
- Each recipe: code sample, expected behavior, common pitfalls.

### v1.0-G2 — Docusaurus website

- `https://zeroalloc-net.github.io/ZeroAlloc.ORM/`.
- README + Quick Start + Cookbook + Diagnostics reference.
- DocFX-generated API reference page.

### v1.0-G3 — release-please bump to `1.0.0`

- Tag, NuGet publish, announcement.
- ZA.Mediator's release-please config is the template.

---

## P3 — Post-1.0 (v2 backlog)

### ORM-V2-1 — `ZeroAlloc.ORM.Results` package

- Detects `Task<Result<T, E>>` return types.
- Emits Result-wrapped versions (null → `Result.Failure(NotFound)`, exception → `Result.Failure(Infrastructure)`).
- ZA.Results integration deferred from v1.0.

### ORM-V2-2 — `ZeroAlloc.ORM.Validation` package

- Pre-execution validation pipeline.
- `[ValidateParameters]` attribute on methods.
- Only if real adopter demand surfaces.

### ORM-V2-3 — SQL-parser-based analyzer

- Validates SQL column count vs materialization arity at compile time.
- Validates SQL parameter usage.
- Likely uses Microsoft.SqlServer.TransactSql.ScriptDom or similar provider-specific parser.

### ORM-V2-4 — Table-valued parameters

- SQL Server `READONLY` table type binding.
- Postgres array parameters (`int[]`, `text[]`).

### ORM-V2-5 — Schema-drift detection (opt-in)

- Separate analyzer package requiring a DB connection at compile time.
- Validates C# types against actual DB schema.
- Optional via MSBuild property.

### ORM-V2-6 — `out`/`ref` ergonomic sugar for sprocs

- If named-tuple shape turns out to be friction.
- Generator emits a helper method with `out` params that delegates to the underlying async tuple-returning method.

### ORM-V2-7 — `BigInteger`, `Half`, `Int128`/`UInt128` built-ins

- Broaden the v1.0 primitive catalog.

---

## Open questions deferred to implementation

These need resolution but don't block v0.1 startup:

- **Generator-side resource discovery** (for `[Query(FromResource = true)]`) — namespace convention TBD. Likely settles when writing first integration test.
- **Diagnostic doc URLs** — need doc site live before generator emits real `helpLinkUri`. Stub to GitHub markdown until then.
- **GitHub Actions permissions** — verify NuGet API key + release-please permissions land cleanly in the new repo.
- **Per-milestone design re-check** — each milestone release should pause to compare emit against the design doc and update the doc with any divergence.

---

## Cross-cutting concerns

### ZA.Mapping adoption of `ZeroAlloc.TypeConversions`

- Separate PR on `ZeroAlloc.Mapping` repo after ZA.ORM v0.2 ships.
- Both libraries reference the same TypeConversions major.
- Bumps to TypeConversions force coordinated releases of both downstream libraries.
- Adds to ZA.Mapping repo's backlog: "Adopt ZeroAlloc.TypeConversions, retire duplicate convention catalog."

### AdoNet.Async dependencies

- ZA.ORM PackageReference uses `[1.*]` floating-minor on AdoNet.Async (current ZA-repo pattern).
- AdoNet.Async major bumps require a matching ZA.ORM release in the same window.
- AdoNet.Async PR #101 (AOT) + PR #102 (`IAsyncDbBatch`) already merged — both required before ZA.ORM v0.1 starts.

### ZA.Telemetry composition (no runtime coupling)

ZA.ORM does NOT ship its own ActivitySource. Adopters who want observability declare
a repository interface decorated with ZA.Telemetry's `[Instrument]` + `[Trace]` /
`[Count]` / `[Histogram]` attributes, implement it as a ZA.ORM-annotated partial class,
and DI-wire the ZA.Telemetry-generated proxy on top of the ZA.ORM implementation.

Both libraries' source generators run independently with zero coupling. No ZA.Telemetry
dependency in ZA.ORM's package graph. Pattern documented in `docs/cookbook/observability.md`
(lands in v0.6).

### ZeroAlloc.Templates adoption

- After ZA.ORM v1.0 ships, the templates' `OrderRepository` (za-clean) and `GetOrderHandler` raw-SQL path (za-vertical-slice, post-B5) should migrate from hand-written ADO.NET to ZA.ORM-annotated partial methods.
- Templates backlog gets a new entry: "B8 (TBD) — Migrate template OrderRepository to ZeroAlloc.ORM."
- Validates the design end-to-end. Discovers any v1.0 gaps that need a v1.1 release.

---

## How items get added here

Any new finding during the brainstorm, design implementation, or adoption surfaces as a new entry. Use the same priority bands (P0 / P1 / P2 / P3) and milestone tagging if it fits, or "Cross-cutting" / "Open questions" if it doesn't. Items get crossed out with `~~ORM-X1 — ...~~ — ✅ shipped X.Y.Z (link)` when complete.
