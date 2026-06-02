# Top-Level `Task<List<T>>` / `Task<IList<T>>` Return Shapes — Design

**Status:** approved 2026-06-02
**Scope:** ZeroAlloc.ORM generator, additive
**Closes:** carry-forward note #2 from the v1.3 BulkInsert PR (#106)
**Branch:** `feat/orm-toplevel-list-shapes` off `main` at `b988179`

## Background

PR [#104](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/pull/104) added `EmitShape.ListResultSet` — `Task<IReadOnlyList<T>>` as a top-level partial-method return shape, drained into a buffered `List<T>`. The classifier gate only matches `IReadOnlyList`1`. `Task<List<T>>` and `Task<IList<T>>` are rejected as Unknown despite the emit being shape-agnostic.

## Change

Extend the classifier branch at `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs:1295-1311` to also accept these two element-type shapes from `System.Collections.Generic`:

- `List`1`
- `IList`1`

The condition becomes:

```csharp
if (inner is INamedTypeSymbol listInner
    && listInner.Arity == 1
    && listInner.TypeArguments.Length == 1
    && string.Equals(
        listInner.ContainingNamespace?.ToDisplayString(),
        "System.Collections.Generic",
        StringComparison.Ordinal)
    && listInner.MetadataName is "IReadOnlyList`1" or "List`1" or "IList`1")
```

The element-materialization fallback chain (FlatRow → DomainEntity → Unknown) is unchanged. `EmitListResultSet` is untouched; its `var __list = new List<T>(); … return __list;` body satisfies all three target types via implicit reference conversion.

## Excluded (with rationale)

| Shape | Reason |
|---|---|
| `IEnumerable<T>` | Overlaps with the `Streaming` shape (`IAsyncEnumerable<T>`). Accepting it as a buffered list would silently choose the wrong execution model. |
| `ICollection<T>` | Niche. Users wanting this presumably want `List<T>`. Add on demand. |
| `IReadOnlyCollection<T>` | Strictly weaker than `IReadOnlyList<T>`. No use case surfaced. |

## Tests

Two new snapshot tests in the existing list-shape test file (location: same directory as the `IReadOnlyList` snapshot from #104, found via Glob during planning):

- `ListResultSet_Task_List_emits_buffered_list_shape`
- `ListResultSet_Task_IList_emits_buffered_list_shape`

Both mirror the existing `IReadOnlyList<T>` snapshot test inputs, only changing the partial-method return type. Generated emit bodies differ only in the method signature line.

## Out of scope

- No new diagnostics
- No new model fields (the shape variant is invisible to materialization)
- No public-API additions (Abstractions untouched)
- No cookbook changes (existing recipes already describe the shape contract correctly; the new variants are transparent extensions)

## Commit shape

Single commit: `feat(generator): accept Task<List<T>> and Task<IList<T>> as top-level partial return shapes`. Conventional-commit `feat:` so release-please rolls this into a v1.3.1 release PR alongside the unreleased follow-ups from PR #108.
