# Composite-in-List Emit Recursion Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Add non-nullable composite support to `EmitListResultSet` by mirroring `EmitFlatRow`'s composite-branch, and narrow the classifier guard from `HasCompositeBinding` → `HasNullableCompositeColumn` so only nullable composites still route to ZAO022. Closes the last PR #109 carry-forward.

**Architecture:** Five tasks: (1) modify `EmitListResultSet` + narrow the classifier guard in `OrmGenerator.cs`; (2) add 3 snapshot tests to `ListResultSetTests.cs`; (3) update or replace the existing `CompositeDetectionTests.Task_of_List_of_composite_row_emits_ZAO022` test from PR #109; (4) full-suite sweep + verify 266+ existing snapshots stay byte-identical; (5) push + PR + admin-merge with `feat(generator):` squash for release-please v1.6.0 minor bump.

**Tech Stack:** C# 13 / .NET 10 / Roslyn incremental generators / Verify (`.verified.cs` snapshots) / xunit.v3.

**Reference design doc:** `docs/plans/2026-06-03-composite-in-list-emit-design.md` (committed `c9c3ad0` on this branch).

**Working branch:** `feat/composite-in-list-emit` (already created off `main` at `7fe3898`).

> **Local SDK pin gotcha:** `global.json` pins SDK `10.0.300 latestFeature`; dev machine has 10.0.204 max. Before any `dotnet` invocation:
> ```powershell
> (Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
> ```
> Revert with `git checkout global.json` before each commit. **Never commit the relaxed pin.**

---

### Task 1: Modify `EmitListResultSet` + narrow the classifier guard

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs`

**Step 1: Re-read the current `EmitListResultSet` body**

```
Read OrmGenerator.cs around lines 5979-6062
```

Confirm the current per-column loop emits one `__reader.{col.GetterMethod}({ordinalExpr})` per column with no `InnerColumns` branch — this is what we're fixing.

**Step 2: Re-read `EmitFlatRow`'s composite branch (lines 5147-5167) for the structural template**

```
Read OrmGenerator.cs around lines 5147-5167
```

Note the pattern:
- `var ordinal = 0;` cursor (separate from the loop index `i`)
- `if (col.InnerColumns.Length > 0)` branch
- Composite call: `EmitNestedCompositeConstruction(sb, col, ordinal, indent, trailing); ordinal += col.InnerColumns.Length; continue;`
- Leaf call: `BuildPositionalReadExpression(col, ordinal); ordinal++;`

**Step 3: Modify the per-column loop in `EmitListResultSet`**

Find the existing per-column loop (around line 6020):

```csharp
sb.AppendLine($"                __list.Add(new {mat.TargetTypeFullName}(");
for (var i = 0; i < cols.Length; i++)
{
    var col = cols[i];
    var trailing = i == cols.Length - 1 ? "));" : ",";
    string ordinalExpr;
    if (useColumnNames)
    {
        ordinalExpr = hoistedOrdinals![i]![0];
    }
    else
    {
        ordinalExpr = $"{i}";
    }
    var readExpr = $"__reader.{col.GetterMethod}({ordinalExpr})";
    // ... convention logic ...
    // ... nullable logic ...
    sb.AppendLine($"                    {expr}{trailing}");
}
```

Use Edit to insert a composite-branch + replace `$"{i}"` with `$"{ordinal}"` + add `ordinal++` at the end. The replacement:

- `old_string`:
  ```csharp
          sb.AppendLine($"                __list.Add(new {mat.TargetTypeFullName}(");
          for (var i = 0; i < cols.Length; i++)
          {
              var col = cols[i];
              var trailing = i == cols.Length - 1 ? "));" : ",";
              string ordinalExpr;
              if (useColumnNames)
              {
                  ordinalExpr = hoistedOrdinals![i]![0];
              }
              else
              {
                  ordinalExpr = $"{i}";
              }
              var readExpr = $"__reader.{col.GetterMethod}({ordinalExpr})";
  ```
- `new_string`:
  ```csharp
          sb.AppendLine($"                __list.Add(new {mat.TargetTypeFullName}(");
          // v1.6 — composite columns (InnerColumns.Length > 0) take the
          // nested-construction branch, mirroring EmitFlatRow's composite handling.
          // The `ordinal` cursor advances by inner.Length per composite so following
          // leaf columns continue from the right reader offset.
          var ordinal = 0;
          for (var i = 0; i < cols.Length; i++)
          {
              var col = cols[i];
              var trailing = i == cols.Length - 1 ? "));" : ",";
              if (col.InnerColumns.Length > 0)
              {
                  if (useColumnNames)
                  {
                      EmitNestedCompositeConstructionByOrdinalNameWithHoisted(
                          sb, col, hoistedOrdinals![i]!, "                    ", trailing);
                  }
                  else
                  {
                      EmitNestedCompositeConstruction(sb, col, ordinal, "                    ", trailing);
                      ordinal += col.InnerColumns.Length;
                  }
                  continue;
              }
              string ordinalExpr;
              if (useColumnNames)
              {
                  ordinalExpr = hoistedOrdinals![i]![0];
              }
              else
              {
                  ordinalExpr = $"{ordinal}";
              }
              var readExpr = $"__reader.{col.GetterMethod}({ordinalExpr})";
  ```

Then add `ordinal++;` after the existing `sb.AppendLine($"                    {expr}{trailing}");` at the end of the loop body. The full end-of-loop edit:

- `old_string`:
  ```csharp
              sb.AppendLine($"                    {expr}{trailing}");
          }
          sb.AppendLine("            }");
          sb.AppendLine("            return __list;");
  ```
- `new_string`:
  ```csharp
              sb.AppendLine($"                    {expr}{trailing}");
              ordinal++;
          }
          sb.AppendLine("            }");
          sb.AppendLine("            return __list;");
  ```

**Step 4: Verify `EmitNestedCompositeConstruction` signature matches the call**

```
Grep for "private static void EmitNestedCompositeConstruction" in OrmGenerator.cs
```

Expected signature: `(StringBuilder sb, ColumnBinding composite, int ordinal, string indent, string trailing)`. The call above uses 5 args; if the signature is different (e.g. takes `int startOrdinal` instead of `int ordinal`), adapt. Read the helper to confirm.

Similarly verify `EmitNestedCompositeConstructionByOrdinalNameWithHoisted` signature — should accept `(StringBuilder sb, ColumnBinding composite, string[] innerOrdinalLocals, string indent, string trailing)` per line 5869.

**Step 5: Narrow the classifier guard**

Find the `HasCompositeBinding` call sites in the `ListResultSet` classifier (around line 1295-1340 — the gate added in PR #109 commit `d8d412f`).

Grep for `HasCompositeBinding(listFlat.Columns)` and `HasCompositeBinding(listDomain.Columns)` (or similar names — confirm the actual field names by reading the surrounding code).

Use Edit on each call site. The shape will be roughly:

- `old_string`:
  ```csharp
                  if (HasCompositeBinding(listFlat.Columns))
                      return (EmitShape.Unknown, ...);
  ```
- `new_string`:
  ```csharp
                  // v1.6 — non-nullable composites are now handled by EmitListResultSet's
                  // composite branch. Only NULLABLE composites still need the rejection;
                  // their per-iteration hoisted-locals pattern is a v1.7+ feature.
                  if (HasNullableCompositeColumn(listFlat.Columns))
                      return (EmitShape.Unknown, ...);
  ```

Apply analogously to the DomainEntity branch.

**Step 6: Decide on `HasCompositeBinding` helper fate**

```
Grep for "HasCompositeBinding(" in OrmGenerator.cs after Step 5
```

If there are zero remaining call sites: **delete the helper** (lines 5189-5202 area per the file we read earlier). It was added in PR #109 specifically for the ListResultSet classifier; removal cleans up dead code.

If there are remaining call sites: leave the helper alone.

**Step 7: Verify build green + existing tests pass**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build src/ZeroAlloc.ORM.Generator/ZeroAlloc.ORM.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj -c Release
```

Expected:
- Build: green
- Existing 266 snapshot tests: **all pass with 0 `.received.cs` files**

The change inserts a new composite branch in the per-column loop. Existing tests don't exercise composite-in-list (PR #109's classifier guard rejected them), so their snapshots SHOULD stay byte-identical.

**The `$"{i}"` → `$"{ordinal}"` swap is a no-op for non-composite shapes** because `ordinal == i` when no composites advance the cursor. If snapshots diff, something's off — STOP and inspect.

If existing test `CompositeDetectionTests.Task_of_List_of_composite_row_emits_ZAO022` FAILS at this stage: that's expected — Task 3 handles it. Don't fix yet.

**Step 8: Revert + commit**

```powershell
git checkout global.json
git add src/ZeroAlloc.ORM.Generator/OrmGenerator.cs
git commit -m "feat(generator): recurse into composite InnerColumns in EmitListResultSet (v1.6)

Mirrors EmitFlatRow's composite-branch (lines 5158-5163) into the
ListResultSet emit so non-nullable composite columns in
Task<IReadOnlyList<TRow>> / Task<List<TRow>> / Task<IList<TRow>>
materialize correctly via nested 'new TypeName(reader.GetX(N), ...)'
construction. Uses the existing EmitNestedCompositeConstruction +
EmitNestedCompositeConstructionByOrdinalNameWithHoisted helpers.

Narrows the classifier guard from HasCompositeBinding to
HasNullableCompositeColumn — non-nullable composites now pass through
to emit; nullable composites still route to ZAO022 pending v1.7+
hoisted-locals adaptation for per-iteration while-body emit.

Closes the last carry-forward from PR #109."
```

If `HasCompositeBinding` was deleted in Step 6, mention it in the commit body.

---

### Task 2: Add 3 snapshot tests for the new + still-rejected paths

**Files:**
- Modify: `tests/ZeroAlloc.ORM.Generator.Tests/Emit/ListResultSetTests.cs`

**Step 1: Read the existing `ListResultSetTests.cs`**

```
Read tests/ZeroAlloc.ORM.Generator.Tests/Emit/ListResultSetTests.cs
```

Confirm:
- Existing tests use `GeneratorHarness.RunGenerator(source)` + `Verify(...)` shape
- The using-block + class layout (FlatRow record + DomainEntity class snapshots already exist for the non-composite cases)

**Step 2: Append 3 new tests**

Insert before the closing brace of the class:

```csharp
[Fact]
public Task ListResultSet_FlatRow_with_NonNullable_Composite_emits_recursed_construction()
{
    // v1.6 — Task<IReadOnlyList<OrderRow>> where OrderRow has a non-nullable
    // Money composite column. Expects emit to include
    // `new global::TestApp.Money(__reader.GetDecimal(1), __reader.GetString(2))`
    // inside the row construction.
    var source = """
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public sealed record Money(decimal Amount, string Currency);
        public sealed record OrderRow(int Id, Money Total);

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT Id, Amount, Currency FROM Orders ORDER BY Id")]
            public partial Task<IReadOnlyList<OrderRow>> ListOrdersAsync(CancellationToken ct);
        }
        """;
    return Verify(GeneratorHarness.RunGenerator(source));
}

[Fact]
public Task ListResultSet_DomainEntity_with_NonNullable_Composite_emits_recursed_construction()
{
    // v1.6 — same as above but DomainEntity shape (column-name path uses
    // hoisted ordinal locals via EmitNestedCompositeConstructionByOrdinalNameWithHoisted).
    var source = """
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public sealed record Money(decimal Amount, string Currency);
        public sealed class Order
        {
            public Order(int Id, Money Total) { this.Id = Id; this.Total = Total; }
            public int Id { get; }
            public Money Total { get; }
        }

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT Id, Amount, Currency FROM Orders")]
            public partial Task<IReadOnlyList<Order>> ListOrdersAsync(CancellationToken ct);
        }
        """;
    return Verify(GeneratorHarness.RunGenerator(source));
}

[Fact]
public Task ListResultSet_with_Nullable_Composite_still_rejected()
{
    // v1.6 — nullable composites in list rows are NOT yet supported.
    // The HasNullableCompositeColumn classifier guard still routes them
    // to ZAO022. Expect the generator output to reflect that rejection
    // (Unknown emit shape + ZAO022 diagnostic).
    var source = """
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public sealed record Money(decimal Amount, string Currency);
        public sealed record OrderRow(int Id, Money? Total);

        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT Id, Amount, Currency FROM Orders")]
            public partial Task<IReadOnlyList<OrderRow>> ListOrdersAsync(CancellationToken ct);
        }
        """;
    return Verify(GeneratorHarness.RunGenerator(source));
}
```

> **Adapt** the test source if the existing test patterns use `[ValueObject]`-decorated `Money` instead of a plain record (check the prior FlatRow snapshot tests for what shapes the harness expects).

**Step 3: First run — Verify drops `.received.txt` / `.received.cs` files**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj --filter "FullyQualifiedName~ListResultSet_FlatRow_with_NonNullable_Composite|FullyQualifiedName~ListResultSet_DomainEntity_with_NonNullable_Composite|FullyQualifiedName~ListResultSet_with_Nullable_Composite_still_rejected"
```

Expected: 3 failed tests with "no Verify baseline" + 3 `.received.cs` files dropped.

**Step 4: Inspect each received snapshot**

For each of the 3 new files:

1. **`...FlatRow_with_NonNullable_Composite...`**: confirm the emit includes `new global::TestApp.Money(__reader.GetDecimal(1), __reader.GetString(2))` inside the `__list.Add(...)` call. The outer `OrderRow` construction wraps it: `__list.Add(new global::TestApp.OrderRow(__reader.GetInt32(0), new global::TestApp.Money(__reader.GetDecimal(1), __reader.GetString(2))));`

2. **`...DomainEntity_with_NonNullable_Composite...`**: confirm column-name path emits ordinal-hoist locals like `var __Amount_ord = __reader.GetOrdinal("Amount");` and the composite construction references them: `new global::TestApp.Money(__reader.GetDecimal(__Amount_ord), __reader.GetString(__Currency_ord))`.

3. **`...with_Nullable_Composite_still_rejected...`**: should contain a `ZAO022` diagnostic dump (Unknown shape) NOT a chunked emit body. The classifier guard fired correctly.

If any test produces unexpected output, STOP and report.

**Step 5: Promote received → verified**

```powershell
Get-ChildItem tests/ZeroAlloc.ORM.Generator.Tests -Filter "ListResultSetTests.ListResultSet*Composite*.received.*" -Recurse | ForEach-Object {
    $newName = $_.Name -replace '\.received\.', '.verified.'
    Move-Item $_.FullName (Join-Path $_.Directory $newName) -Force
}
```

**Step 6: Re-run to confirm 3/3 green**

```powershell
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj --filter "FullyQualifiedName~ListResultSet"
```

Expected: 5/5 passed (2 baseline ListResultSet + 3 new composite tests).

**Step 7: Revert + commit**

```powershell
git checkout global.json
git add tests/ZeroAlloc.ORM.Generator.Tests/Emit/ListResultSetTests.cs
git add tests/ZeroAlloc.ORM.Generator.Tests/Snapshots/ListResultSetTests.*Composite*.verified.cs
git commit -m "test(generator): 3 snapshot tests for composite-in-list emit

Covers:
  - FlatRow non-nullable composite (record + record shape, positional ordinal)
  - DomainEntity non-nullable composite (named ordinals via hoisted locals)
  - Nullable composite still routes to ZAO022 (HasNullableCompositeColumn guard)

Verified by visual inspection of the .received.cs files generated by
Verify on first run."
```

---

### Task 3: Update or replace the existing `CompositeDetectionTests` ZAO022 test

**Files:**
- Modify or replace: `tests/ZeroAlloc.ORM.Generator.Tests/Emit/CompositeDetectionTests.cs`

The test `Task_of_List_of_composite_row_emits_ZAO022` (added in PR #109 commit `d8d412f`) asserts that `Task<List<TRow>>` with composite TRow fires ZAO022. After Task 1, **non-nullable composites no longer fire ZAO022** — they now emit correctly.

**Step 1: Read the existing test**

```
Read tests/ZeroAlloc.ORM.Generator.Tests/Emit/CompositeDetectionTests.cs around the Task_of_List_of_composite_row_emits_ZAO022 test
```

Inspect: does the TRow use a **nullable** composite (`Money? Total`) or **non-nullable** (`Money Total`)?

**Step 2: Decide on update vs replace**

- **If TRow uses NON-nullable composite**: the test's premise no longer holds. Two options:
  - (a) **Update** the test to use `Money? Total` and keep the assertion (ZAO022 still fires for nullables). Rename the test to `Task_of_List_of_NULLABLE_composite_row_emits_ZAO022`.
  - (b) **Delete** the test entirely. Task 2's `ListResultSet_with_Nullable_Composite_still_rejected` covers the assertion already; the duplication isn't useful.
  
  Prefer **(b) delete** — less test code, single source of truth for the ZAO022 assertion.

- **If TRow already uses NULLABLE composite**: no change needed. Test still passes.

**Step 3: Apply the chosen update**

If (b) — delete: use Edit to remove the test method (and surrounding XML doc / comments if relevant). Or remove the whole file if it only had that one test.

If (a) — update: change the TRow shape from `Money Total` → `Money? Total` and rename the test method.

**Step 4: Verify**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj --filter "FullyQualifiedName~CompositeDetectionTests"
```

Expected: all tests in `CompositeDetectionTests` pass.

**Step 5: Revert + commit**

```powershell
git checkout global.json
git add tests/ZeroAlloc.ORM.Generator.Tests/Emit/CompositeDetectionTests.cs
git commit -m "test(generator): <update|remove> Task_of_List_of_composite_row ZAO022 test

After v1.6's composite-in-list emit recursion (prior commit),
non-nullable composites in list rows emit correctly instead of
firing ZAO022. The existing CompositeDetectionTests assertion is
now obsolete; <option (a) updates it to use nullable composite |
option (b) removes it because Task 2's
ListResultSet_with_Nullable_Composite_still_rejected covers
the same territory>."
```

---

### Task 4: Full-suite sweep + verify all 266+ existing snapshots stay byte-identical

**Files:** none (verification only)

**Step 1: Run the full Generator.Tests suite**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj -c Release
```

Expected: **269 passed** (266 baseline + 3 new from Task 2), or **268 passed** if Task 3's option (b) removed one (266 baseline - 1 removed + 3 new = 268).

**Step 2: Check for stray `.received.cs` files**

```powershell
Get-ChildItem tests/ZeroAlloc.ORM.Generator.Tests -Filter "*.received.*" -Recurse | Measure-Object | Select-Object -ExpandProperty Count
```

Expected: **0**. If non-zero, some existing snapshot drifted unexpectedly.

**If existing snapshots drifted:** the `$"{i}"` → `$"{ordinal}"` swap from Task 1 is the prime suspect. For non-composite lists, `ordinal` should equal `i` at every position, so emit should be byte-identical. If it isn't, inspect the diff:

- If the diff is just a variable reference (`{ordinal}` vs `{i}`) — that's only a problem if the snapshots are sensitive to literal text. They shouldn't be (the EMIT is the snapshot; both produce the same `__reader.GetInt32(0)`).
- If the diff is real (ordinal cursor diverged), STOP and investigate.

**Step 3: Run the other test projects**

```powershell
dotnet test tests/ZeroAlloc.ORM.Abstractions.Tests/ZeroAlloc.ORM.Abstractions.Tests.csproj -c Release
dotnet test tests/ZeroAlloc.ORM.Tests/ZeroAlloc.ORM.Tests.csproj -c Release
dotnet test tests/ZeroAlloc.TypeConversions.Tests/ZeroAlloc.TypeConversions.Tests.csproj -c Release
dotnet test tests/ZeroAlloc.ORM.Integration.Tests/ZeroAlloc.ORM.Integration.Tests.csproj -c Release --filter "Category!=Postgres"
git checkout global.json
```

Expected: all pass (per the xunit-v3 migration's baseline: 10 + 11 + 44 + 114 = 179).

**Step 4: No commit yet — verification only.** Proceed to Task 5.

---

### Task 5: Push + PR + admin-merge

**Files:** none (workflow only)

**Step 1: Pre-flight log check**

```powershell
git log --oneline main..HEAD
```

Expected 4 commits (in order):
1. `c9c3ad0` docs(design): composite-in-list emit recursion for v1.6
2. `<plan>` docs(plan): composite-in-list emit recursion implementation plan
3. `<task1>` feat(generator): recurse into composite InnerColumns in EmitListResultSet (v1.6)
4. `<task2>` test(generator): 3 snapshot tests for composite-in-list emit
5. `<task3>` test(generator): <update|remove> Task_of_List_of_composite_row ZAO022 test

**Step 2: Final sweep**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build ZeroAlloc.ORM.slnx -c Release
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj -c Release
git checkout global.json
git status
```

Expected: build green, 268 or 269 generator tests pass, working tree clean.

**Step 3: Push**

```powershell
git push -u origin feat/composite-in-list-emit
```

**Step 4: Open the PR**

```powershell
$prBody = @'
## Summary

Closes the last carry-forward from PR #109: ``Task<IReadOnlyList<TRow>>`` / ``Task<List<TRow>>`` / ``Task<IList<TRow>>`` with non-nullable composite-bearing TRow now emit correct row materialization instead of routing to ZAO022.

## What changes

**`EmitListResultSet`** (`OrmGenerator.cs:5979-6062`):
- Per-column loop now branches on ``col.InnerColumns.Length > 0`` and calls ``EmitNestedCompositeConstruction`` (positional) or ``EmitNestedCompositeConstructionByOrdinalNameWithHoisted`` (column-name) — mirroring ``EmitFlatRow``''s composite-branch at lines 5158-5163.
- Adds an ``ordinal`` cursor (separate from the loop index ``i``) that advances by ``inner.Length`` per composite, so following leaf columns continue from the right reader offset.

**Classifier guard** (around line 1322):
- Replaces ``HasCompositeBinding(...)`` rejection with ``HasNullableCompositeColumn(...)``. Non-nullable composites now pass through to emit; nullable composites still route to ZAO022 (deferred to v1.7+ — per-iteration hoisted-locals adaptation needed).
- ``HasCompositeBinding`` helper <removed if unused | left as-is>.

**Tests:**
- 3 new snapshot tests in ``ListResultSetTests``:
  - FlatRow non-nullable composite (positional ordinal path)
  - DomainEntity non-nullable composite (named ordinals via hoisted locals)
  - Nullable composite still rejected (HasNullableCompositeColumn guard)
- ``CompositeDetectionTests.Task_of_List_of_composite_row_emits_ZAO022`` from PR #109 <updated to use nullable composite | removed; covered by Task 2''s nullable-rejected test>.

## Test plan

- [x] All 266 baseline Generator.Tests snapshots stay byte-identical (the ``$"{i}"`` → ``$"{ordinal}"`` swap is a no-op for non-composite shapes)
- [x] 3 new snapshot tests pass
- [x] All other test projects unaffected (Abstractions 10, ORM 11, TypeConversions 44, Integration 114)
- [ ] CI: lint + build-test + collision-smoke + aot-publish-smoke

## Note for release-please

``feat(generator):`` commit. Default release-please config triggers a minor bump → **v1.6.0**. **Squash title MUST start with `feat:`** (recurring release-please gotcha).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@

gh pr create --title "feat(generator): recurse into composite InnerColumns in EmitListResultSet (v1.6)" --body $prBody
```

Capture the PR number.

**Step 5: Monitor CI**

```powershell
gh pr checks <PR_NUMBER> --watch
```

Expected: 4 checks green (`lint`, `build-test`, `collision-smoke`, `aot-publish-smoke`).

If a check fails, investigate before retrying. The xunit-v3 migration (PR #114, just merged) is the most-recent change to the test infra; if any of those CI steps was test-sensitive, that's the likely culprit. Read the log.

**Step 6: Admin-merge once green**

```powershell
gh pr merge <PR_NUMBER> --squash --delete-branch --admin
```

Squash title must start with `feat(generator):`. The PR title already does — `gh pr merge --squash`'s default-to-PR-title behavior is correct.

**Step 7: Verify post-merge**

```powershell
git checkout main
git pull --ff-only
git log --oneline -3
```

Expected: new squashed `feat(generator): ...` commit on top.

**Step 8: Wait for release-please**

```powershell
Start-Sleep -Seconds 60
gh pr list --state open --search "release-please"
```

Expected: a fresh `chore(main): release 1.6.0` PR opens (or an existing one refreshes if accumulated commits were waiting). User merges separately + triggers pack-push for v1.6.0 via the established workflow.

## Report

- PR URL/number
- CI check results
- Merge SHA on `main`
- release-please PR number (proposing v1.6.0)
- Whether `HasCompositeBinding` helper was removed (or kept)
- Whether the existing `CompositeDetectionTests` test was updated or removed
- Anything unexpected

Do NOT push fixes blindly to CI failures. Investigate first.

---

## Out of scope (deliberately not in this plan)

- **Nullable composite in list rows** — defer to v1.7+; needs per-iteration hoisted-locals adaptation
- **Composite columns in `IAsyncEnumerable<T>` (Streaming) shape** — may have the same gap; out of scope; file follow-up if found broken
- **Refactoring shared row-construction** across FlatRow / Streaming / ListResultSet into a single helper — separate clean-up, no immediate benefit
- **Public API changes** — none

## When the plan is complete

The branch `feat/composite-in-list-emit` has 5 commits (1 design + 1 plan + 1 emit + 1 tests + 1 existing-test-update) + the merge squash on main. release-please opens v1.6.0 release PR. User merges + manually triggers `gh workflow run pack-push.yml -f version=1.6.0`.
