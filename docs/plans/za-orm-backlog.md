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

### v0.3-CLN1 — Hoist `GetOrdinal` once per column in column-name materialization

- Source: PR #42 code review (2026-05-31).
- Affects all column-name emit paths (DomainEntity in Streaming, FlatRow, plus the
  single-row DomainEntity path) — wherever the emit currently produces both
  `__reader.IsDBNull(__reader.GetOrdinal("Col"))` and
  `__reader.GetXxx(__reader.GetOrdinal("Col"))`, `GetOrdinal` runs twice per row per
  column in the hot loop.
- Fix: emit `var __o_Col = __reader.GetOrdinal("Col");` once before the materialization
  body and reuse the local in both the `IsDBNull` and `GetXxx` calls.
- Cross-shape (FlatRow + DomainEntity + Streaming-DomainEntity), so treat as a single
  pass rather than per-shape patches. Snapshot churn will be wide — bundle with other
  emit cleanups if possible.

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

### v0.3-CLN3 — `IAsyncDbConnection.CanCreateBatch` not forwarded for Sqlite

- Source: PR #44 fix-up (2026-05-31).
- `Microsoft.Data.Sqlite` ≥9.x exposes `CanCreateBatch = true` on `SqliteConnection`,
  but `AdoNet.Async.Adapters.AsAsync()` wrapper reports `CanCreateBatch = false`.
- Consequence: `MultiResultSetTests` Auto + Never variants both exercise the
  `;`-joined fallback branch — no integration test in the Sqlite suite actually
  drives the `IAsyncDbBatch` path. Batch-branch emit is still proven via the
  generator snapshot test `MultiResultSetAutoTests.Tuple_with_record_and_list_emits_runtime_CanCreateBatch_branch`.
- Fix options:
  1. If the adapter SHOULD forward `CanCreateBatch` from the underlying connection,
     file as an upstream bug in AdoNet.Async and bump once fixed.
  2. If the adapter intentionally gates this on its own batch-shape support, add a
     Postgres / SQL Server integration fixture (already on the v0.5 / v0.6 roadmap)
     that exercises the batch branch in CI.
- File-header comment in `MultiResultSetTests.cs` records this state honestly.

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

### v0.5-T1 — Multi-column composite materialization

- `Money(decimal Amount, string Currency)` — two columns per composite.
- Generator tracks expanded column count beyond C# ctor arity.
- Nested in flat rows: `record OrderRow(int Id, Money Total)` → 3 columns.

### v0.5-T2 — Multi-column composite binding

- Method parameter typed `Money total` → SQL params `@total_Amount` + `@total_Currency`.
- Naming convention via positional unpacking (no name-coupling to ctor args).

### v0.5-T3 — `[Materialize(Factory = "X")]` resolution

- Explicit factory lookup by name on the type.
- Map factory's parameters to columns by name.
- ZAO043 if not found.

### v0.5-T4 — Nullable composite handling

- All-null composite columns → `null`.
- Partial-null → `ZeroAllocOrmMaterializationException` at runtime + ZAO050 compile-time warning.

---

## P1 — Milestone v0.6 (3-4 days): diagnostics polish + observability composition recipe

### v0.6-T2 — Full diagnostics catalog audit

- All ZAO codes ZAO001-ZAO070 emit correctly.
- `helpLinkUri` points to per-code docs.
- Each diagnostic has a unit test (1 case where it should fire, 1 where it should not).

### v0.6-T4 — `docs/diagnostics/` reference pages

- One markdown file per ZAO code with: trigger, fix hint, code example, related codes.
- Pre-published on `main` so the `helpLinkUri` resolves.

### v0.6-T5 — ZA.Telemetry composition cookbook recipe

Single `docs/cookbook/observability.md` page showing the `[Instrument]` interface + `[Query]` partial class composition pattern. ZA.ORM ships no ActivitySource of its own; observability lives at consumer seam via ZA.Telemetry.

---

## P2 — Milestone v0.7 (1 week): benchmarks + collision + polish

### v0.7-T1 — Benchmark suite

- `tests/ZeroAlloc.ORM.Benchmarks/` with BDN.
- Comparisons: hand-written ADO.NET (baseline), Dapper.AOT, ZeroAlloc.ORM.
- Workloads: single-row read, multi-row read, head+lines (multi-result), insert.
- Run on Sqlite in-memory AND Postgres via Testcontainers.

### v0.7-T2 — ZA.Rest collision smoke test

- `tests/ZeroAlloc.ORM.GeneratorCollision.AotSmoke/`.
- One project references both `ZeroAlloc.Rest.Generator` and `ZeroAlloc.ORM.Generator`, uses both, AOT-publishes.
- `.github/workflows/collision-smoke.yml` runs this on every PR.
- **Gates v1.0 release.**

### v0.7-T3 — README + Quick Start

- Mirror AdoNet.Async's README structure.
- Packages table with AOT compatibility column.
- Quick Start covers the 4 most common annotation shapes.
- "NativeAOT compatibility" section.

### v0.7-T4 — API review pass

- Walk every public type in `Abstractions` + `ORM`.
- Confirm no accidental publics (use `internal` aggressively).
- Confirm naming consistency.
- Snapshot the public API surface for v1.0 lock.

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
