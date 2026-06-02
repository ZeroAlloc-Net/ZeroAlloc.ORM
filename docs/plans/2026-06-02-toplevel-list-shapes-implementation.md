# Top-Level `Task<List<T>>` / `Task<IList<T>>` Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Extend ZA.ORM's `ListResultSet` classifier (PR #104) to accept `Task<List<T>>` and `Task<IList<T>>` as top-level partial return shapes, alongside the existing `Task<IReadOnlyList<T>>`.

**Architecture:** Single-line classifier-gate widening at `OrmGenerator.cs:1295-1311`. The emit method (`EmitListResultSet`) is unchanged — it already produces `List<T>` and returns it via implicit conversion. Two new snapshot tests guard the new shapes against regression.

**Tech Stack:** C# 13 / .NET 10 / Roslyn incremental generators / Verify (`.verified.cs` snapshots) / xUnit.

**Reference design doc:** `docs/plans/2026-06-02-toplevel-list-shapes-design.md` (committed `c603331` on this branch).

**Working branch:** `feat/orm-toplevel-list-shapes` (already created off `main` at `b988179`).

> **Local SDK pin gotcha** (recurring from BulkInsert PR, see prior plan): `global.json` pins SDK `10.0.300 latestFeature`; dev machine has 10.0.204 max. Before any local `dotnet` command, temporarily relax `global.json` to `10.0.100` (keeps the `latestFeature` rollForward — `10.0.204` satisfies that):
>
> ```powershell
> (Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
> ```
>
> After verification, **`git checkout global.json` to revert before `git commit`**. The relaxed pin must never reach a commit.

---

### Task 1: Widen the classifier gate to accept `List<T>` and `IList<T>`

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs:1295-1311`

**Step 1: Read the current gate**

Open `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs` and read lines 1289–1311 to confirm the current shape. You should see:

```csharp
// v1.2 — Task<IReadOnlyList<TRow>>: bare top-level list return. …
if (inner is INamedTypeSymbol listInner
    && string.Equals(listInner.MetadataName, "IReadOnlyList`1", StringComparison.Ordinal)
    && listInner.Arity == 1
    && listInner.TypeArguments.Length == 1
    && string.Equals(listInner.ContainingNamespace?.ToDisplayString(), "System.Collections.Generic", StringComparison.Ordinal))
{
    // … FlatRow / DomainEntity fallback …
}
```

**Step 2: Replace the metadata-name check**

Replace the single-name `string.Equals(listInner.MetadataName, "IReadOnlyList`1", StringComparison.Ordinal)` clause with the three-name OR. The minimal Edit:

- `old_string`:
  ```csharp
          && string.Equals(listInner.MetadataName, "IReadOnlyList`1", StringComparison.Ordinal)
  ```
- `new_string`:
  ```csharp
          && listInner.MetadataName is "IReadOnlyList`1" or "List`1" or "IList`1"
  ```

Also update the v1.2 comment block above the `if` to reflect the widened shape. Replace the comment block from "v1.2 — Task<IReadOnlyList<TRow>>: bare top-level list return." through to its end with a note covering all three shapes:

- `old_string`:
  ```csharp
          // v1.2 — Task<IReadOnlyList<TRow>>: bare top-level list return. Single
          // result set drained into a buffered List<TRow>. Distinct from
          // MultiResultSet (which uses a tuple shape with List as one element kind)
          // and from Streaming (IAsyncEnumerable, yield-based, no buffering).
          // Element materialization reuses the FlatRow / DomainEntity models so
          // positional records and named-column classes both work. Issue #102.
  ```
- `new_string`:
  ```csharp
          // v1.2 — bare top-level list return. Single result set drained into a
          // buffered List<TRow>. Accepted shapes: Task<IReadOnlyList<T>> (v1.2,
          // issue #102), Task<List<T>>, Task<IList<T>> (v1.3.1, follow-up to
          // PR #106). All three target types accept the emit's `var __list =
          // new List<T>(); … return __list;` body via implicit conversion.
          // Distinct from MultiResultSet (tuple shape) and from Streaming
          // (IAsyncEnumerable, yield-based). Element materialization reuses
          // the FlatRow / DomainEntity models so positional records and
          // named-column classes both work.
  ```

**Step 3: Verify build green**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build src/ZeroAlloc.ORM.Generator/ZeroAlloc.ORM.Generator.csproj -c Release
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

**Step 4: Existing tests still green**

```powershell
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj
```

Expected: **258/258 passed**. The classifier change is additive — no existing shape routes differently. **If any existing snapshot diff appears, STOP** — the new shapes must be purely additive.

**Step 5: Revert global.json + commit**

```powershell
git checkout global.json
git add src/ZeroAlloc.ORM.Generator/OrmGenerator.cs
git commit -m "feat(generator): accept Task<List<T>> and Task<IList<T>> as top-level partial return shapes

Widens the ListResultSet classifier gate (PR #104, originally
Task<IReadOnlyList<T>> only) to also accept Task<List<T>> and
Task<IList<T>>. Emit is unchanged — already produces List<T> and
returns it via implicit conversion to all three target types.

Closes carry-forward note 2 from the BulkInsert PR #106."
```

---

### Task 2: Two new snapshot tests covering the new shapes

**Files:**
- Modify: `tests/ZeroAlloc.ORM.Generator.Tests/Emit/ListResultSetTests.cs`
- Snapshots will land in `tests/ZeroAlloc.ORM.Generator.Tests/Snapshots/` on first run

**Step 1: Append the two new tests**

Add to the bottom of `ListResultSetTests.cs` (before the closing brace of the class), mirroring the existing `ListResultSet_with_FlatRow_record_element_emits_buffered_drain` test:

```csharp
[Fact]
public Task ListResultSet_Task_List_emits_buffered_list_shape()
{
    var source = """
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public sealed record OrderListRow(int Id, int CustomerId, decimal Total);

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT Id, CustomerId, Total FROM Orders ORDER BY Id LIMIT @limit OFFSET @offset")]
            public partial Task<List<OrderListRow>> ListOrdersAsync(
                int limit, int offset, CancellationToken ct);
        }
        """;
    return Verify(GeneratorHarness.RunGenerator(source));
}

[Fact]
public Task ListResultSet_Task_IList_emits_buffered_list_shape()
{
    var source = """
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public sealed record OrderListRow(int Id, int CustomerId, decimal Total);

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT Id, CustomerId, Total FROM Orders ORDER BY Id LIMIT @limit OFFSET @offset")]
            public partial Task<IList<OrderListRow>> ListOrdersAsync(
                int limit, int offset, CancellationToken ct);
        }
        """;
    return Verify(GeneratorHarness.RunGenerator(source));
}
```

**Step 2: Run the tests — expect "no Verify baseline"**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj --filter "FullyQualifiedName~ListResultSetTests"
```

Expected: 4 tests, 2 passed (the existing IReadOnlyList tests), **2 failed with "no Verify baseline"** messages and `.received.cs` files dropped under `Snapshots/`.

**Step 3: Inspect each received snapshot**

Locate the new files:
```powershell
Get-ChildItem tests/ZeroAlloc.ORM.Generator.Tests -Filter "ListResultSetTests.ListResultSet_Task_*.received.*" -Recurse
```

Read each `.received.cs` file. Verify these properties hold:

1. The emit body is a real chunked `EmitListResultSet` body (not a TODO stub or Unknown diagnostic dump). Look for `await using var __cmd = __conn.CreateCommand();`, `await using var __reader = await __cmd.ExecuteReaderAsync(...)`, `var __list = new global::System.Collections.Generic.List<global::TestApp.OrderListRow>();`, `while (await __reader.ReadAsync(...).ConfigureAwait(false))` drain loop, `return __list;` at the end.
2. The partial-method signature **matches the test's declared return type**:
   - `List<...>` test: `partial async Task<global::System.Collections.Generic.List<global::TestApp.OrderListRow>> ListOrdersAsync(...)`
   - `IList<...>` test: `partial async Task<global::System.Collections.Generic.IList<global::TestApp.OrderListRow>> ListOrdersAsync(...)`
3. The emit body is **otherwise byte-identical** to the existing `ListResultSet_with_FlatRow_record_element_emits_buffered_drain` snapshot — only the method signature line differs.

If any property is violated, STOP and investigate — a hidden emit-side dependency on the return shape would indicate the design's "emit is shape-agnostic" claim was wrong.

**Step 4: Promote received → verified**

```powershell
Get-ChildItem tests/ZeroAlloc.ORM.Generator.Tests -Filter "ListResultSetTests.ListResultSet_Task_*.received.*" -Recurse | ForEach-Object {
    $newName = $_.Name -replace '\.received\.', '.verified.'
    Move-Item $_.FullName (Join-Path $_.Directory $newName) -Force
}
```

**Step 5: Rerun — confirm 4/4 green**

```powershell
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj --filter "FullyQualifiedName~ListResultSetTests"
```

Expected: 4/4 passed.

**Step 6: Full generator suite still green**

```powershell
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj
```

Expected: **260/260 passed** (258 baseline + 2 new).

**Step 7: Revert + commit**

```powershell
git checkout global.json
git add tests/ZeroAlloc.ORM.Generator.Tests/Emit/ListResultSetTests.cs
git add tests/ZeroAlloc.ORM.Generator.Tests/Snapshots/ListResultSetTests.ListResultSet_Task_*.verified.cs
git commit -m "test(generator): snapshot coverage for Task<List<T>> and Task<IList<T>> shapes

Two new snapshots mirroring the existing IReadOnlyList<T> shape, only
the partial-method return type differs. Guards the classifier-gate
widening from the prior commit against regression."
```

---

### Task 3: Push + PR + admin-merge

**Step 1: Pre-flight commit log check**

```powershell
git log --oneline main..HEAD
```

Expected (3 commits, in order):
1. `docs(design): top-level List<T> / IList<T> return shapes for ListResultSet`
2. `feat(generator): accept Task<List<T>> and Task<IList<T>> as top-level partial return shapes`
3. `test(generator): snapshot coverage for Task<List<T>> and Task<IList<T>> shapes`

**Step 2: Full sweep build + test**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build ZeroAlloc.ORM.slnx -c Release
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj -c Release
dotnet test tests/ZeroAlloc.ORM.Integration.Tests/ZeroAlloc.ORM.Integration.Tests.csproj -c Release
git checkout global.json
git status
```

Expected:
- Build: green, 0 errors
- Generator: 260 passed
- Integration: 112 passed, 1 skipped (pre-existing)
- Working tree clean (global.json reverted)

**Step 3: Push**

```powershell
git push -u origin feat/orm-toplevel-list-shapes
```

**Step 4: Open the PR**

```powershell
$prBody = @'
## Summary

Closes carry-forward note 2 from PR #106 (CommandKind.BulkInsert v1.3 — merged + released as v1.3.0). Extends the ListResultSet classifier (originally `Task<IReadOnlyList<T>>` only, from PR #104) to also accept `Task<List<T>>` and `Task<IList<T>>`.

## What changes

- **Classifier** (`OrmGenerator.cs:1295-1311`): widens the metadata-name check from `"IReadOnlyList`1"` to `"IReadOnlyList`1" or "List`1" or "IList`1"`.
- **Emit unchanged**: `EmitListResultSet` already produces `var __list = new List<T>(); … return __list;`, which converts implicitly to all three target types.
- **Tests**: 2 new snapshots mirroring the existing IReadOnlyList snapshot — only the partial-method return type differs.

## Excluded (deliberate)

- `IEnumerable<T>` — overlaps with the Streaming shape (`IAsyncEnumerable<T>`); accepting as a buffered list would silently choose the wrong execution model.
- `ICollection<T>` / `IReadOnlyCollection<T>` — niche; add on demand.

## Test plan

- [x] Generator tests passing (260 = 258 + 2 new)
- [x] Integration tests passing (112)
- [ ] CI build-test + aot-publish-smoke + collision-smoke

## Note for release-please

This is a `feat:` commit; release-please should open a v1.3.1 release PR rolling up this change plus the unreleased follow-ups from PR #108 (parity test, ZAO071 granularity, ctor-only VO Identity classifier fix). PR #108 was squash-titled `chore:` and skipped the release tracker.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@

gh pr create --title "feat: accept Task<List<T>> and Task<IList<T>> as top-level partial return shapes" --body $prBody
```

Capture the PR number.

**Step 5: Monitor CI**

```powershell
gh pr checks <PR_NUMBER>
```

Expected check set: `lint`, `build-test`, `collision-smoke`, `aot-publish-smoke`. Wait for all to land green. Do not push fixes blindly — investigate any failure first.

**Step 6: Admin-merge once green**

The repo's branch protection requires reviews, and PR author can't self-approve. Use `--admin` per the established pattern on this branch (#103, #104, #106, #108):

```powershell
gh pr merge <PR_NUMBER> --squash --delete-branch --admin
```

> **Squash title:** when prompted (or via the `gh` UX), make sure the squash *title* starts with `feat:` — not `chore:`. If `gh` defaults to the PR title, that's already correct. The release-please tracker reads the squashed commit's title (not its body), so a `feat:`-prefixed squash title is what triggers the v1.3.1 release PR.

**Step 7: Verify post-merge state**

```powershell
git checkout main
git pull --ff-only
git log --oneline -3
```

Expected: the new squashed commit on top of `main`, with `feat:` prefix.

**Step 8: Confirm release-please picks up the change**

Wait 1-5 minutes after merge, then check whether release-please has opened a v1.3.1 release PR:

```powershell
gh pr list --state open --search "release-please"
```

Expected: a PR titled something like `chore(main): release 1.3.1` proposing the v1.3.1 release. If it does NOT appear within 5 minutes, check the release-please workflow run for errors:

```powershell
gh run list --workflow=release-please --limit 3
```

Triggering pack-push to publish `ZeroAlloc.ORM 1.3.1` to NuGet is **out of scope** for this task — the user merges the release-please PR and runs the manual workflow at their discretion.

---

## Out of scope (deliberately not in this plan)

- Accepting `IEnumerable<T>` as a top-level buffered list shape (would conflict with Streaming).
- Accepting `ICollection<T>` / `IReadOnlyCollection<T>` (niche; defer until an adopter asks).
- Cookbook updates (existing recipes describe the contract correctly; the new shapes are transparent extensions).
- Documentation of the new shapes in `docs/cookbook/streaming.md` or `commands.md` (out of scope; can be a separate small PR if adopter feedback indicates confusion).

## When the plan is complete

The branch `feat/orm-toplevel-list-shapes` has 3 commits, all CI checks pass, the PR is squash-merged with a `feat:` title, and release-please has opened the v1.3.1 release PR rolling up this change alongside the unreleased follow-ups from PR #108.
