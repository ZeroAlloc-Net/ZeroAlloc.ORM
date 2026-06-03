# Composite-in-List Emit Recursion — Design

**Status:** approved 2026-06-03
**Scope:** ZeroAlloc.ORM generator, additive emit + classifier narrowing
**Target version:** v1.6.0
**Closes:** carry-forward from PR #109
**Branch:** `feat/composite-in-list-emit` off `main` at `7fe3898` (post-xunit-v3)

## Background

PR #109 widened the `ListResultSet` classifier (line 1289-1340 in `OrmGenerator.cs`) to accept `Task<List<T>>` / `Task<IList<T>>` alongside the existing `Task<IReadOnlyList<T>>`. During implementation, the snapshot tests caught that `EmitListResultSet` does **not** recurse into composite `InnerColumns` the way `EmitFlatRow` does. The pragmatic fix at the time: add a `HasCompositeBinding` classifier guard that rejects composite-bearing rows uniformly across all three list shapes, routing them to ZAO022.

This closed the immediate correctness issue but left a feature gap: `Task<IReadOnlyList<OrderRow>>` where `OrderRow(int Id, Money Total)` and `Money` is a composite VO can't be expressed via ZA.ORM.

## Decision

Adopt Approach A from the brainstorm: **mirror `EmitFlatRow`'s non-nullable-composite branch into `EmitListResultSet`** so non-nullable composite columns emit correctly. Narrow the classifier guard from `HasCompositeBinding` → `HasNullableCompositeColumn`, so only **nullable** composites still route to ZAO022. Nullable-composite-in-list rows are deferred to a future iteration (the hoisted-locals pattern from FlatRow needs adaptation for per-iteration emit inside a `while` loop — doable but bigger scope).

## What changes

**Files modified (1 source + 1 test):**

1. **`src/ZeroAlloc.ORM.Generator/OrmGenerator.cs`** — three surgical changes:

   **(a)** In `EmitListResultSet` (line ~6020-6057) — the per-column loop. Mirror `EmitFlatRow`'s composite handling at lines 5158-5163:
   
   ```csharp
   var ordinal = 0;
   for (var i = 0; i < cols.Length; i++)
   {
       var col = cols[i];
       var trailing = i == cols.Length - 1 ? "));" : ",";
       
       // NEW — composite branch
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
       
       // EXISTING — leaf-column path (current code)
       string ordinalExpr;
       if (useColumnNames)
       {
           ordinalExpr = hoistedOrdinals![i]![0];
       }
       else
       {
           ordinalExpr = $"{ordinal}";   // changed from $"{i}" → $"{ordinal}" to track flattened cursor
       }
       // ... rest of existing read/convention/nullable logic ...
       ordinal++;
   }
   ```
   
   Indent prefix `"                    "` (20 spaces) matches the existing emit's inside-while indent.

   **(b)** Update `EmitOrdinalHoistsForColumns` call site if needed — the existing helper at line 5915 already handles composite `InnerColumns` correctly (returns a jagged shape). Should work as-is for the column-name path.

   **(c)** Narrow the classifier rejection at the `HasCompositeBinding` call site (around line 1322-1340 from PR #109's commit `d8d412f`):
   
   - Replace `HasCompositeBinding(listFlat.Columns)` with `HasNullableCompositeColumn(listFlat.Columns)` for both FlatRow and DomainEntity branches.
   - Optionally delete `HasCompositeBinding` if it's no longer referenced elsewhere — but check first; it may be used by other classifier paths.

2. **`tests/ZeroAlloc.ORM.Generator.Tests/Emit/ListResultSetTests.cs`** — add 3 snapshot tests:

   - **`ListResultSet_FlatRow_with_NonNullable_Composite_emits_recursed_construction`** — `Task<IReadOnlyList<OrderRow>>` where `OrderRow(int Id, Money Total)` and `Money` is a positional record-struct composite. Expects emit to include `new global::TestApp.Money(__reader.GetDecimal(1), __reader.GetString(2))` inside the row construction.
   - **`ListResultSet_DomainEntity_with_NonNullable_Composite_emits_recursed_construction`** — same but DomainEntity shape (column-name path uses hoisted ordinal locals).
   - **`ListResultSet_with_Nullable_Composite_still_rejected`** — `Task<IReadOnlyList<TRow>>` where `TRow(int Id, Money? Total)`. Asserts ZAO022 fires (nullable-composite-in-list deferred).

   The existing `CompositeDetectionTests.Task_of_List_of_composite_row_emits_ZAO022` test from PR #109 (commit `d8d412f`) needs review:
   - If its TRow uses **non-nullable** composite, the test's assertion is now wrong (emit succeeds instead of ZAO022). **Update** the test source to use `Money?` so the rejection still fires, OR **delete** it and let the new tests cover the territory.
   - If it already uses nullable composite, no change needed.

## Versioning + release

- `feat(generator):` commit — adds support for previously-rejected adopter shapes
- release-please cuts **v1.6.0** (minor — additive emit capability)
- Squash title at merge: `feat(generator):` so release-please fires

## Tests + acceptance

- Existing 266 Generator.Tests snapshots stay byte-identical (the change is opt-in via composite-bearing TRow types; non-composite list tests unaffected)
- 3 new snapshot tests cover the new positive paths + the still-rejected nullable case
- Existing composite-detection test (PR #109) updated to use nullable-composite-only or replaced
- All 5 test projects pass (445 → 448 with the 3 new + 0-1 removed)

## What stays out of scope

- **Nullable composite in list rows** — defer to v1.7+ if requested. The hoisted-locals pattern from `EmitFlatRowWithHoistedLocals` needs adaptation to live inside a per-iteration `while` body. Doable but bigger emit change with its own snapshot risk.
- **Refactoring shared row-construction** across FlatRow / Streaming / ListResultSet into a single `EmitRowConstruction` helper — Approach B from the brainstorm. Future clean-up, no immediate benefit.
- **Composite columns in `IAsyncEnumerable<T>` (Streaming) shape** — `EmitStreaming` may have the same gap; out of this PR's scope. File as a follow-up if found broken.
- **Public API changes** — none.

## Risk

- **Existing snapshot stability**: the change inserts a new branch in the per-column loop. Existing tests don't exercise composite-in-list (the guard rejected them), so their snapshots should stay byte-identical. The ordinal-cursor change from `$"{i}"` → `$"{ordinal}"` is a **noop for non-composite lists** because `ordinal == i` when no composites advance it further. Verify via the full snapshot suite.
- **EmitNestedCompositeConstruction signature**: confirm during implementation that the helper accepts a `string indent` parameter and emits the composite construction at the right indent for the inside-while body of ListResultSet (20 spaces vs FlatRow's 16 spaces).
- **`HasCompositeBinding` references**: search for callers before deleting; it may be used by other shapes (Streaming, MultiResultSet element-type checks).
