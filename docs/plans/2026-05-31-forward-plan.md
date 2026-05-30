# ZeroAlloc.ORM — Forward Plan (post-v0.1-review)

> **Status:** Approved 2026-05-31. Drives the next ~2 weeks of work to ship `0.1.0` cleanly, then continues with the v0.2-v0.7 roadmap.
>
> **Triggered by:** the 25-finding pre-release code review (5 P0 / 10 P1 / 10 P2) of the v0.1 milestone work (65 commits, 75 tests passing). Findings can't be addressed in a single batch — this plan slices them into 11 small PRs (R1-R11) plus the release tag itself (R12), each independently reviewable.

---

## Context

v0.1 implementation work is complete per [`docs/plans/2026-05-30-v0.1-implementation.md`](2026-05-30-v0.1-implementation.md) — all 8 phases shipped (Phase 0 test infra through Phase 7 AOT smoke + Phase 8 release prep). The code is in `main`. The release-please PR proposing `0.1.0` is open.

The pre-release code review (logged in conversation transcript 2026-05-30) surfaced findings that fall into three structural categories:

1. **Public API surface vs implementation gap** — `Abstractions` package ships attributes/properties the generator silently ignores. Locking these at v0.1 means promising adopters behavior the v0.1 generator doesn't deliver.
2. **Packaging discipline** — two NuGet packages would ship empty; the release-please workflow doesn't actually push to NuGet.
3. **Quality gaps** — the #1 design invariant (EF-style ref-counted connection lifecycle) has no test coverage; `helpLinkUri` on every diagnostic 404s on day 1.

The forward plan addresses **all 25 findings** before `0.1.0` tags, per the project policy of "fix all findings, minor included." The 11 PRs are sized to land in 1-3 commits each.

---

## Decisions taken (so the plan is unambiguous)

- **R1 path (Trim public API):** **Strip** `[Command]`, `[StoredProcedure]`, `[Materialize]`, `[StoreAsString]` from `0.1.0`. Each re-lands additively in the milestone that implements it. Keep `[Query]` + `[Param]` only. For `[Query(FromResource)]` + `[Query(Batch)]` properties (which are needed in the v0.1 attribute shape but not yet implemented): keep them, emit `ZAO0Ni`-level info diagnostics saying "deferred to v0.x".
   - **Rationale:** matches the memory entry *"Prefer additive `[Obsolete]` over breaking renames — minimize major bumps; reach for additive deprecation first."* Adding the attributes back in v0.2/v0.3/v0.4 is non-breaking. Locking them at v0.1 with no working implementation behind them creates forever-friction for adopters.

- **R2 path (Slim packages):** **Drop** `ZeroAlloc.ORM.Analyzers` from `release-please-config.json` for v0.1 (re-introduce when ZAO010+ rules land — likely v0.6). **Move** `PrimitiveCatalog.cs` from `src/ZeroAlloc.ORM.Generator/Catalog/` into `src/ZeroAlloc.TypeConversions/` (makes that package non-empty + aligns with the design's "shared catalog" intent).

---

## Phase R — Release readiness for `0.1.0`

Each row below is one PR. Each PR is 1-3 commits, scoped to one concern, independently reviewable. After all 11 land + are merged, R12 tags the release.

### R1 — Trim public API to what v0.1 delivers

**Removes from `Abstractions` (v0.1 scope):**

- `CommandAttribute.cs` + `CommandKind.cs` → deleted. Re-introduced in v0.4.
- `StoredProcedureAttribute.cs` → deleted. Re-introduced in v0.4.
- `MaterializeAttribute.cs` + `MaterializeStrategy.cs` → deleted. Re-introduced in v0.5.
- `StoreAsStringAttribute.cs` → deleted. Re-introduced in v0.2.

**Diagnostics added:**

- `ZAO020` info — `[Query(FromResource = true)]` is deferred to v0.x (informational only).
- `ZAO021` info — `[Query(Batch = ...)]` non-Auto values deferred to v0.3.

**Tests:** delete the attribute test files for the removed types. Update Phase 1 bundle test counts (drops from 20 to ~10 Abstractions tests).

**Generator:** remove `LookupDescriptor` mappings for `ZAO020`/`ZAO021` from any switches; add them to the descriptor catalog as `Info`-severity. Note: ZAO020 fires from `TransformMethod` when `FromResource = true` in the attribute's named arguments; ZAO021 fires similarly for non-Auto `Batch`.

**Commits:**

1. `refactor(abstractions)!: strip [Command]/[StoredProcedure]/[Materialize]/[StoreAsString] from v0.1 surface`
2. `feat(generator): add ZAO020/ZAO021 info diagnostics for unimplemented [Query] options`

### R2 — Slim package list + move PrimitiveCatalog

**Drop `ZeroAlloc.ORM.Analyzers` from v0.1:**

- Edit `release-please-config.json` — remove the `src/ZeroAlloc.ORM.Analyzers` entry.
- Edit `.release-please-manifest.json` — remove the corresponding line.
- Edit `ZeroAlloc.ORM.slnx` — remove the Analyzers project entry.
- Delete `src/ZeroAlloc.ORM.Analyzers/` directory.
- Update README packages table — remove the Analyzers row.

**Move `PrimitiveCatalog`:**

- `git mv src/ZeroAlloc.ORM.Generator/Catalog/PrimitiveCatalog.cs src/ZeroAlloc.TypeConversions/PrimitiveCatalog.cs`
- Update the file's namespace from `ZeroAlloc.ORM.Generator.Catalog` to `ZeroAlloc.TypeConversions`.
- Update the call sites in `OrmGenerator.cs` (`using ZeroAlloc.TypeConversions;`).
- The existing `ProjectReference` from Generator → TypeConversions now has real content to consume.

**Commit:** `refactor(packaging): drop ORM.Analyzers from v0.1, hoist PrimitiveCatalog into TypeConversions`

### R3 — NuGet publish wiring

**Add a tag-triggered pack-push workflow:**

- `.github/workflows/pack-push.yml` (modeled on AdoNet.Async or ZA.Mediator).
- Triggered on `release: published` (release-please creates GitHub releases via the action).
- `dotnet pack -c Release` for all 4 v0.1 packages.
- `dotnet nuget push *.nupkg --source nuget.org --api-key ${{ secrets.NUGET_API_KEY }} --skip-duplicate`.

**Prerequisite:** verify `NUGET_API_KEY` org secret is configured on the new repo. If not, add it from the ZeroAlloc-Net org's existing key.

**Commit:** `ci: add tag-triggered NuGet publish workflow`

### R4 — Connection-lifecycle correctness coverage

**Add to `tests/ZeroAlloc.ORM.Integration.Tests/`:**

- `LifecycleTests.cs` — passes a closed `IAsyncDbConnection`, invokes the repo method, asserts:
  1. The query succeeds (returns the seeded row).
  2. `connection.State == ConnectionState.Closed` afterwards.
  3. A second invocation also succeeds (proves close-then-reopen works).

The fixture's `InitializeAsync()` currently auto-opens; this test creates the connection manually without opening, exercising the `__openedHere = true` branch.

**Commit:** `test(integration): verify EF-style connection lifecycle on closed connection`

### R5 — Diagnostic docs stubs + URI integrity

**Create 9 markdown files** in `docs/diagnostics/`, one per shipping ZAO code (001-009) plus the 2 new info diagnostics from R1 (020, 021). Each follows the template:

```markdown
# ZAO001 — Annotated method must be partial

**Severity:** Error  
**Category:** ZeroAlloc.ORM

## Trigger

A method annotated with `[Query]` (or `[Command]`, `[StoredProcedure]` in later versions) is not declared `partial`.

## Fix

Add the `partial` modifier to the method declaration.

## Example

(positive + negative C# snippets)

## Related

- ZAO004 — Containing type must be partial.
```

**Commit:** `docs(diagnostics): add ZAO001-ZAO009 + ZAO020/ZAO021 reference pages`

### R6 — Codegen polish bundle

- **P1-9:** add `<IsTrimmable>true</IsTrimmable>` to `ZeroAlloc.ORM.csproj` and `ZeroAlloc.ORM.Abstractions.csproj`. Implicit under `IsAotCompatible=true` but the design doc explicitly enumerates both.
- **P1-10:** add `[global::System.CodeDom.Compiler.GeneratedCode("ZeroAlloc.ORM.Generator", "<version>")]` to each emitted partial-method declaration. Version sourced from a generator constant; update the constant in this PR.
- **P1-3:** fix `EquatableArray<T>.GetHashCode` to return the same constant when `IsDefault` and when `Length == 0` — eliminates the equality-vs-hash mismatch.
- **P1-4:** replace `yield`-based `GetEnumerator` with a struct enumerator wrapping `ImmutableArray<T>.Enumerator`. Reduces allocation pressure on the design-time generator path.
- **P2-6:** add `ZAO022` info diagnostic — `EmitShape.Unknown` fallback (e.g. `Task<List<T>>` in v0.1 has no emit; consumer would see CS8795 with no ZA-specific guidance).
- **P2-5:** reconcile design doc Section 2 (which says "CancellationToken must be last") with the implementation (which tolerates CT anywhere). My read: update the design doc to match the implementation, since the existing tests cover non-last CT.

**Commits (one per item, except P1-9/10 bundled):**

1. `feat(orm): declare IsTrimmable=true + emit [GeneratedCode] on partial methods`
2. `fix(generator): EquatableArray equality/hash consistency for default vs empty`
3. `perf(generator): struct enumerator on EquatableArray to reduce design-time allocations`
4. `feat(generator): add ZAO022 info diagnostic for unknown return-type shapes`
5. `docs(design): clarify that CancellationToken may appear at any parameter position`

### R7 — Diagnostic UX polish

- **P1-5:** split ZAO007 messages — different text for "no CT param at all" vs "CT param exists but missing `[EnumeratorCancellation]`". Keep the ID; templated message via different format-string per detection path.
- **P1-6:** ZAO008 SQL `;`-counter — add minimal string-literal awareness (recognize single-quoted `'...'` and double-quoted `"..."` literals before counting `;`). Avoids false positives on `WHERE bio LIKE '%hello;%'`. Alternative: downgrade ZAO008 to Warning. **Pick:** add literal awareness (~15 LOC) — keeps Error severity which is the right contract.
- **P1-7:** when ZAO003 and ZAO004 fire on the same type, suppress ZAO003 (the "no connection" diagnostic) — fix `partial` first, then re-evaluate connection presence.

**Commits (one per item):**

1. `feat(generator): differentiate ZAO007 message based on CT-param presence`
2. `fix(generator): ZAO008 string-literal-aware semicolon counter`
3. `feat(generator): suppress ZAO003 when ZAO004 (type not partial) also fires`

### R8 — Type-scoped diagnostic hoist

Address `QueryMethodModel`'s `TODO(v0.2)` per the design and the P1-2 finding.

**Move from `QueryMethodModel` to `QueryRepositoryModel`:**

- `ContainingTypeName`, `Namespace` (already redundant in v0.1 — original Phase 2.2 TODO).
- `ConnectionAccess`, `ConnectionResolved` (Phase 2.3 additions).
- `ContainingTypePartial`, `ContainingTypeLocation` (Phase 3 ZAO004 additions).

**Eliminate `repo.Methods.Values[0]` fallback** — compute the type-properties once during the grouping step.

**Test:** add a regression case for the "partial in one file, not in another" multi-declaration corner case.

**Commit:** `refactor(generator): hoist type-scoped fields from QueryMethodModel to QueryRepositoryModel`

### R9 — README Quick Start

Replace the placeholder ("Not yet — v0.1 milestone is in progress.") with a Quick Start covering the 2 actually-shipping shapes:

1. `Task<int>` / `Task<T?>` scalar query.
2. `Task<TRow?>` positional-record FlatRow query.

Plus the AOT smoke project's full code block as a reference. Both can be done in ~80 lines of markdown.

Skip multi-result, IAsyncEnumerable, `[Command]`, etc. — they're not in v0.1.

**Commit:** `docs(readme): seed Quick Start with v0.1-shipping shapes`

### R10 — Test infrastructure hardening

- **P2-1:** `GeneratorHarness.RunGenerator` curated reference list. Replace `AppDomain.CurrentDomain.GetAssemblies()` with explicit `typeof(...).Assembly.Location` entries for: `object`, `Task`, `IAsyncDbConnection`, `QueryAttribute`. Add `Basic.Reference.Assemblies` NuGet for the netstandard reference set if the curated list isn't sufficient.
- **P1-1:** FlatRow nullable-column integration test — `record FlexRow(int? OptionalCount, string? OptionalName)`, seed nulls, verify materialization.
- **P2-9:** test method naming sweep — pick `Subject_state_expectation` snake_case as the convention, rename outliers.

**Commits:**

1. `refactor(generator-tests): curated reference list in GeneratorHarness`
2. `test(integration): FlatRow nullable column round-trip`
3. `style(tests): normalize test method naming to Subject_state_expectation`

### R11 — Exception + doc + pin polish

- **P1-8:** add `Exception()` parameterless ctor + `(string, Exception inner)` overload to `ZeroAllocOrmVersionMismatchException`. Add `Exception()` parameterless ctor to `ZeroAllocOrmMaterializationException`. Standard exception convention.
- **P2-8:** add a one-line comment in `Directory.Build.props` (or the generator csproj) explaining the Roslyn 4.13.0 pin (e.g. "pinned to match the .NET 10 SDK's Roslyn-as-a-service version; bump when SDK ships a newer Roslyn").
- **P2-10:** backlog/plan-doc drift — sweep `docs/plans/za-orm-backlog.md` for items that were shipped but not crossed off. Cross-check against `git log --oneline`. Update the "v0.1 implementation status (live)" section.

**Commit:** `chore: polish exception ctors, pin rationale, backlog reconciliation`

### R12 — Release

After R1-R11 all merge:

1. Verify `dotnet test` — should be ≥75 passing (likely 78-82 after the new tests).
2. release-please's open PR auto-rebases to `main` with the new commits → re-runs → produces the `0.1.0` release PR with the complete changelog.
3. Merge it → tag `v0.1.0` fires → R3's `pack-push.yml` workflow runs → 3 NuGet packages publish (`Abstractions`, `ORM`, `Generator`).
4. Verify on https://www.nuget.org/profiles/MarcelRoozekrans.
5. Strike v0.1-T10 in the backlog.

---

## Milestones — adjusted based on v0.1 learnings

### v0.2 — value-objects + enums (~2 weeks)

**Re-add stripped surface (from R1):**
- Add back `StoreAsStringAttribute` (and ship its detection during this milestone).
- (Keep `MaterializeAttribute` stripped — re-adds in v0.5.)

**Original scope holds:**
- ZA.ValueObjects integration in shared `ZeroAlloc.TypeConversions`.
- Single-arg-ctor record discovery.
- Static factory discovery.
- Enum default-int round-trip + `[StoreAsString]` string round-trip.
- Multi-arg domain entity materialization.
- Diagnostics ZAO040-ZAO044.

**Adjustments from v0.1 learnings:**

1. **+15-20% time buffer** for Meziantou/Roslynator/ErrorProne friction (MA0048 file-per-type, MA0004 ConfigureAwait on await using, MA0006 `string.Equals` not `==`). Every PR encountered at least one of these.
2. **Compile-smoke test pattern is now mandatory** — every new `EmitShape` ships with both a `Verify`-based snapshot test AND a `RunGeneratorAndCompile`-based test. Caught 3 real bugs in v0.1 that snapshots missed.
3. **Plan-doc-sync commit at milestone end** — each milestone release closes with a "fold lessons back into the design + plan doc" commit. Plan defects compound; this is cheap insurance.

### v0.3 — multi-result + streaming (~2 weeks)

**Re-add stripped surface (from R1):**
- (No `Abstractions` changes — `[Query(Batch)]` non-Auto is already in the surface; just light up the generator path during this milestone.)

**Original scope holds:** `IAsyncDbBatch` emit, `IAsyncEnumerable<T>` streaming, tuple-of-result-sets dispatch, ZAO032/033 diagnostics.

### v0.4 — commands + sprocs (~2 weeks)

**Re-add stripped surface (from R1):**
- Add back `CommandAttribute` + `CommandKind` enum.
- Add back `StoredProcedureAttribute`.

**Original scope holds:** `[Command]` emit (NonQuery/Scalar/Identity), `[StoredProcedure]` with named-tuple outputs, ZAO060-062.

### v0.5 — composites + custom factories (~1 week)

**Re-add stripped surface (from R1):**
- Add back `MaterializeAttribute` + `MaterializeStrategy` enum.

**Original scope holds:** multi-column composites, `[Materialize(Factory)]` resolution, nullable composite handling.

### v0.6 — observability + diagnostics polish (~1 week)

**Updated scope based on 2026-05-31 decision** — composition with ZA.Telemetry instead of building our own ActivitySource. v0.6 becomes documentation-heavy.

**Original scope dropped:** built-in `ActivitySource`, provider-quirk doc comments in emit (move to v0.7 if still applicable).

**New scope:**

1. Full diagnostics catalog audit (ZAO codes ZAO001-070 each have positive + negative tests; `helpLinkUri` resolves).
2. `docs/diagnostics/` per-code reference pages — expand from v0.1's 11 stubs to the complete catalog.
3. Cookbook recipe at `docs/cookbook/observability.md` — explains the ZA.Telemetry composition pattern with a worked example.

**Time estimate:** ~3-4 days (was ~1 week). The ActivitySource emit work was the bulk; documentation is lighter.

**Re-add stripped package (unchanged from prior plan):** Reintroduce `ZeroAlloc.ORM.Analyzers` package when ZAO010+ rules land — or move existing diagnostics from Generator into Analyzers if the architectural split makes sense by then.

### v0.7 — benchmarks + collision + polish (~1 week)

**Original scope holds.** ZA.Rest collision smoke test gates v1.0 release.

### v1.0 release — API freeze (~1 week)

**Original scope holds.** Cookbook docs, Docusaurus website, release-please bump to `1.0.0`.

---

## Cross-cutting permanent invariants (lessons from v0.1)

These get added to the design doc as Section 4 amendments:

1. **Plan-doc-as-you-go discipline.** Plan defects accumulate. Every implementer should commit corrections to the plan doc alongside their work commits. Saves the next implementer the cost of re-discovering the same gap.
2. **Compile-smoke before snapshot acceptance.** Every snapshot-tested emit shape also has a compile-smoke test asserting the generated code compiles. Caught 3 real bugs in v0.1 that text-only snapshots missed.
3. **`@`-prefix verbatim safety on every emitted identifier reference.** Both parameter values AND `CancellationToken` name forwards. Always works for non-keywords (no-op), prevents future regressions on keyword-named consumer parameters.
4. **No `Location`/`Compilation`/`SyntaxNode`/`ISymbol` in cached generator model types.** `EquatableArray<T>` wrap for collections. `LocationInfo` for diagnostic positions. Cache-correctness verified by Roslyn incremental tracking.

---

## Schedule estimate

- **R1-R11:** 1-2 weeks single-developer pace. Most PRs are ~1-2 commits; the largest (R7 diagnostic UX) is 3.
- **R12 release:** 1 day (waiting for CI + NuGet propagation).

After `v0.1.0` tags:

- v0.2 (~2 weeks)
- v0.3 (~2 weeks)
- v0.4 (~2 weeks)
- v0.5 (~1 week)
- v0.6 (~1 week)
- v0.7 (~1 week)
- v1.0 release (~1 week)

**Total to v1.0:** ~13 weeks single-developer pace from `0.1.0`. Approximately matches the original estimate from the design doc Section 5.

---

## How this plan integrates with the backlog

- `docs/plans/za-orm-backlog.md` remains the canonical "what's shipped + what's open" record.
- This forward plan supersedes the v0.1.T10 line in the backlog (it now requires R1-R12 to land first).
- Each R-numbered PR strikes the corresponding finding off the review log.
- v0.2+ milestone entries in the backlog are updated to reflect the re-add work R1 deferred.
