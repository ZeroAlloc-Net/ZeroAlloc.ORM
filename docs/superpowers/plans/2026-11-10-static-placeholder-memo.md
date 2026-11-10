# Static Placeholder Memo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A repository instance carries no state because of `{name}` or `{col:Name}` placeholders, and a call still allocates nothing after warm-up.

**Architecture:** In `EmitPlaceholderSqlCache` the per-instance field `__sql{i}_{M}` becomes a static memo `__sqlMemo{i}_{M}`. The holder `__ZaoPlaceholderSql` stores the key it was built for. The accessor reads the members on every call and compares them to the memo's key by reference. A miss validates, goes through the existing static `ConcurrentDictionary`, and fills the memo only if empty.

**Tech Stack:** C# incremental source generator (`src/ZeroAlloc.ORM.Generator/OrmGenerator.cs`), xunit, `GeneratorSnapshot`, `ZeroAlloc.TestHelpers` `AllocationGate`.

**Spec:** `docs/design/2026-11-10-static-placeholder-memo-design.md`

## Global Constraints

- Conventional Commits, type `perf:`. Commit body lines at most 100 characters, no nested parentheses, no Claude or Co-Authored-By trailer, no session link.
- No public API change. Generated members stay private. Never add an optional parameter to a shipped signature.
- Output for repositories without placeholder methods stays byte-identical. The `{col}`-only case (no member values, no quote flag) keeps its single static holder, unchanged.
- Keep `ArgumentException.ThrowIfNullOrWhiteSpace` for each `{name}` value, and the `Volatile` publication rule: one immutable holder, `Volatile.Read`, fill-once with `Interlocked.CompareExchange(ref memo, holder, null)`.
- Build and test one project at a time. Run `dotnet test -c Release` as well as Debug. Eight Integration allocation gates already fail in Debug on main; CI runs Release only.
- Do not merge any PR, and never a release PR.

## Review Focus

- A member that returns a fresh but equal string each call: resolves to the same SQL, no wrong table, no allocation. (Task 2)
- A member changed on a live instance: the next call uses the new value. (Task 2)
- Two repositories on different tables alternating: correct SQL for each, zero bytes per call. (Task 2)
- A null or blank member after the memo is filled by another instance: still throws `ArgumentException`. (Task 2)
- `{col:}` with a quote flag flipped between two instances of one repository: each gets its own quoting. (Task 2)

---

### Task 1: Emit the static memo and key-carrying holder

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs` (`EmitPlaceholderSqlHolder` ~6128, `EmitPlaceholderSqlCache` ~6168, and its doc comment)
- Modify: `tests/ZeroAlloc.ORM.Generator.Tests/Emit/PlaceholderEmitTests.cs`
- Modify (regenerate): `tests/ZeroAlloc.ORM.Generator.Tests/Snapshots/PlaceholderEmitTests.Placeholder_Method_Snapshot#TestApp.Repo.g.verified.cs`

**Interfaces:**
- Consumes: existing `PlaceholderSqlName`, `PlaceholderAccess`, `ResolvedSqlExpression`, `QueryMethodModel.PlaceholderSqlAccessorName`.
- Produces: generated per placeholder method with member values or a quote flag: `private static __ZaoPlaceholderSql? __sqlMemo{i}_{M};`, the unchanged static `__sqlCache{i}_{M}` dictionary and accessor `__Sql{i}_{M}()`. The holder constructor becomes `__ZaoPlaceholderSql(string full, string[] parts, string[] keys, bool quote)` with `Keys` and `Quote` properties. No instance field is emitted.

- [ ] **Step 1: Update the emit tests first**
  In `PlaceholderEmitTests.cs`, change `Placeholder_Method_EmitsCacheAndUsesIt` to assert `private static __ZaoPlaceholderSql? __sqlMemo0_CountAsync;` is present, `Assert.DoesNotContain("private __ZaoPlaceholderSql? __sql0_CountAsync", ...)`, `Interlocked.CompareExchange(ref __sqlMemo0_CountAsync` is present, and `Volatile.Write(ref __sql0_` is absent. Add `Placeholder_Method_EmitsNoInstanceField`: for a repository with two placeholder methods, no line matching a non-static field of type `__ZaoPlaceholderSql` exists. Add `Holder_CarriesItsKey`: source contains `public string[] Keys { get; }` and `public bool Quote { get; }`. Add `ColumnPlaceholderOnly_StillUsesOneStaticEntry` assertions only if `ColumnPlaceholderOnly_UsesOneStaticEntry` (line 259) needs the new holder signature; the static holder for that case passes `global::System.Array.Empty<string>()` and `false` as keys and flag.
  Review Focus pin: `QuoteFlagOnly` (line 170) asserts the memo compares `__m.Quote == __v0` (a method with no `{name}` placeholder).

- [ ] **Step 2: Run to verify failure**
  Run: `dotnet test tests/ZeroAlloc.ORM.Generator.Tests -c Release --filter "FullyQualifiedName~PlaceholderEmitTests"`
  Expected: the changed and new tests FAIL.

- [ ] **Step 3: Implement the emit**
  In `EmitPlaceholderSqlHolder`, add `Keys` and `Quote` to the holder and its constructor (signature above). In `EmitPlaceholderSqlCache`, for the keyed case: emit `private static __ZaoPlaceholderSql? {memo};` instead of the instance field; the accessor reads every member into `__v{n}` first (and the quote flag), reads the memo with `Volatile.Read`, and returns it when `__m is not null`, every `ReferenceEquals(__m.Keys[n], __v{n})` holds and `__m.Quote == __v{flag}` (omit the bool test without a flag). On a miss it runs the existing `ThrowIfNullOrWhiteSpace` calls, the existing `GetOrAdd`, whose factory builds the holder with `new string[] { __k... }` as keys and the flag taken from the key, then `Interlocked.CompareExchange(ref memo, __sql, null)` and returns `__sql`. Update the doc comment above `EmitPlaceholderSqlCache` to describe the memo. Name the memo in `PlaceholderSqlName("__sqlMemo", m, methodIndex)`.

- [ ] **Step 4: Run to verify pass, regenerate the snapshot**
  Run the filter from Step 2 until green. If `Placeholder_Method_Snapshot` differs, review the diff of the `.received.cs` file by eye (static memo, key-carrying holder, no instance field) and accept it over the `.verified.cs`. Then run the whole `ZeroAlloc.ORM.Generator.Tests` project in Release and in Debug.
  Expected: all PASS, and no other snapshot changed (`git status` shows only the placeholder snapshot).

- [ ] **Step 5: Commit**
  ```bash
  git add src tests
  git commit -m "perf: keep placeholder SQL in a static memo, not per instance"
  ```
  Body: one line per change, at most 100 characters each, no nested parentheses.

### Task 2: Integration gates, behaviour tests and docs

**Files:**
- Modify: `tests/ZeroAlloc.ORM.Integration.Tests/AllocationGates/TablePlaceholderAllocationTests.cs`
- Modify: `tests/ZeroAlloc.ORM.Integration.Tests/TablePlaceholderTests.cs`
- Modify: `tests/ZeroAlloc.ORM.Integration.Tests/TablePlaceholderRepo.cs` and `ColumnPlaceholderRepo.cs` only if a test needs a settable member or a second table
- Modify: `docs/` page that describes placeholders (find with `grep -ril "{table}" docs`; skip `docs/design` and `docs/plans`), one note

**Interfaces:**
- Consumes: Task 1's generated output. Existing helpers `AllocationGate.MeasureBytesPerCall`, `MeasureBytesPerCallValueTask`, `SqliteFixture`, `TablePlaceholderRepo(conn, table)`, `TableLiteralRepo(conn)`.
- Produces: tests only.

- [ ] **Step 1: Write the failing tests**
  In `TablePlaceholderAllocationTests`, add:
  - `RepositoryInstance_HasNoExtraSizeOverLiteral`: measure `new TablePlaceholderRepo(conn, "Widgets")` and `new TableLiteralRepo(conn)` with `MeasureBytesPerCall` (both warmed with one call); the difference equals the size of the one extra `string` field the placeholder repo stores for its table (8 B on 64-bit). Assert `placeholderBytes - literalBytes == IntPtr.Size`. Give `TablePlaceholderRepo` ten placeholder methods in a second class `TableTenMethodsRepo` (copy the shape of `CountByIdAsync` ten times) and assert its instance size equals `TableLiteralRepo`'s plus `IntPtr.Size`, which fails on 2.16.0 by 80 B.
  - `AlternatingTables_AllocateNothingBeyondLiteral`: create `Widgets` and `Gadgets` tables, two repositories, call them alternately in one measured lambda (`MeasureBytesPerCallValueTask`); assert the bytes equal the sum of two literal calls measured the same way.
  In `TablePlaceholderTests`, add (names are the assertions' subjects):
  - `EqualButDistinctStringReference_ResolvesToSameSql`: `new string("Widgets".AsSpan())` as the table gives the correct count.
  - `ChangedMember_IsHonouredOnNextCall`: a repository whose table member is settable switches from `Widgets` to `Gadgets` and the count reflects the other table.
  - `NullOrBlankMember_ThrowsEvenWhenMemoIsFilled`: fill the memo with one instance, then a second with `null` and one with `"  "` throw `ArgumentException`.
  - `QuoteFlagFlippedBetweenInstances_GetsOwnQuoting`: two instances of the column repository, one quoted, one bare, each executes and returns the row.

- [ ] **Step 2: Run to verify the right ones fail**
  Run: `dotnet test tests/ZeroAlloc.ORM.Integration.Tests -c Release --filter "FullyQualifiedName~TablePlaceholder|FullyQualifiedName~ColumnPlaceholder"`
  Task 1 is already committed, so the new tests should pass at once. To see them fail for the right reason, run them once in a throwaway `git worktree` of `origin/main` with these test files copied in: the ten-method size test fails by 80 B and `ChangedMember_IsHonouredOnNextCall` fails. Report what you saw; do not skip this check silently.

- [ ] **Step 3: Make them pass**
  They should pass with Task 1 as committed. Fix the tests, not the generator, unless a real defect shows; a generator defect goes back to Task 1's code with a new emit test.

- [ ] **Step 4: Docs note**
  In the placeholder docs page add two sentences: members are read on every call, so a changed value is honoured, and a repository instance holds no state for placeholders. Run the docs link or lint step if the repo has one.

- [ ] **Step 5: Full verification, commit**
  Run `dotnet test -c Release` then `dotnet test -c Debug` for `ZeroAlloc.ORM.Integration.Tests` and the Generator tests, one project at a time. Expected: Release all PASS; Debug shows only the 8 known Integration allocation gate failures.
  ```bash
  git add tests docs
  git commit -m "test: gate the instance size and alternating tables for placeholders"
  ```

### Task 3: PR

- [ ] **Step 1:** Rebase on `origin/main`, push `perf/static-placeholder-memo` with `git push -u origin`, open the PR (`perf:` title). Body sections: what changed, a "Behaviour change" section (members read on every call), the measured numbers (instance bytes before and after, the Outbox +80 B cause). No private information, no Claude trailer, no session link. Do not merge. Watch CI with the ci-watch skill.
