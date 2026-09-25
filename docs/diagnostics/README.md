# ZeroAlloc.ORM diagnostic reference

The generator emits compile-time diagnostics with stable `ZAO0NN` codes. Each descriptor's `helpLinkUri` points back into this directory, so clicking the link in your IDE lands on the relevant page.

This table is the **canonical index**: it lists every `DiagnosticDescriptor` currently declared in `src/ZeroAlloc.ORM.Generator/Diagnostics/DiagnosticDescriptors.cs`. `README.md` at the repo root links here instead of duplicating the table, so there is exactly one list to keep in sync. `DiagnosticHelpLinkTests` (in `tests/ZeroAlloc.ORM.Generator.Tests/Diagnostics/`) enforces two things: every descriptor's `helpLinkUri` resolves to a real, non-empty page below, and every descriptor has a row in this table — a missing page or a missing row fails the build.

| Code | Severity | Title | Page |
|------|----------|-------|------|
| ZAO001 | Error | Annotated method must be partial | [ZAO001](ZAO001.md) |
| ZAO002 | Error | Unsupported return type | [ZAO002](ZAO002.md) |
| ZAO003 | Error | No `IAsyncDbConnection` found on containing type | [ZAO003](ZAO003.md) |
| ZAO004 | Error | Containing type must be partial | [ZAO004](ZAO004.md) |
| ZAO005 | Error | Multiple ORM attributes on one method | [ZAO005](ZAO005.md) |
| ZAO006 | Warning | Method has multiple `CancellationToken` parameters | [ZAO006](ZAO006.md) |
| ZAO007 | Error | `IAsyncEnumerable<T>` return without `[EnumeratorCancellation]` | [ZAO007](ZAO007.md) |
| ZAO008 | Error | Multi-statement SQL with single-result return type | [ZAO008](ZAO008.md) |
| ZAO009 | Warning | Redundant `async` keyword on generated partial | [ZAO009](ZAO009.md) |
| ZAO020 | Info | `[Query](FromResource = true)` not yet implemented | [ZAO020](ZAO020.md) |
| ZAO022 | Info | Return type shape not yet supported | [ZAO022](ZAO022.md) |
| ZAO032 | Error | Tuple arity exceeds SQL statement count | [ZAO032](ZAO032.md) |
| ZAO033 | Error | SQL statement count exceeds tuple arity | [ZAO033](ZAO033.md) |
| ZAO040 | Error | No construction strategy resolved for type | [ZAO040](ZAO040.md) |
| ZAO041 | Error | No binding strategy resolved for parameter | [ZAO041](ZAO041.md) |
| ZAO042 | Error | `[StoreAsString]` requires an enum type | [ZAO042](ZAO042.md) |
| ZAO043 | Error | `[Materialize(Factory)]` references missing method | [ZAO043](ZAO043.md) |
| ZAO044 | Error | Ambiguous convention discovery | [ZAO044](ZAO044.md) |
| ZAO050 | Warning | Nullable composite type requires runtime all-or-nothing check | [ZAO050](ZAO050.md) |
| ZAO051 | Error | Factory parameter does not match any SELECT column | [ZAO051](ZAO051.md) |
| ZAO052 | Error | Recursive composite types are not supported | [ZAO052](ZAO052.md) |
| ZAO060 | Error | `[StoredProcedure]` async method has out/ref parameter (reserved, never emitted) | [ZAO060](ZAO060.md) |
| ZAO061 | Error | `[StoredProcedure]` name is empty | [ZAO061](ZAO061.md) |
| ZAO062 | Warning | Named-tuple field does not match any parameter | [ZAO062](ZAO062.md) |
| ZAO063 | Error | `[Param(Name = ...)]` override is not supported on composite parameters | [ZAO063](ZAO063.md) |
| ZAO064 | Info | `[StoredProcedure(Batch = ...)]` non-default value is ignored | [ZAO064](ZAO064.md) |
| ZAO065 | Warning | Decimal output parameter has no `Scale` | [ZAO065](ZAO065.md) |
| ZAO066 | Error | `[Param]` member does not apply to this parameter | [ZAO066](ZAO066.md) |
| ZAO067 | Error | Return value parameter must be `int` | [ZAO067](ZAO067.md) |
| ZAO068 | Error | More than one return value parameter | [ZAO068](ZAO068.md) |
| ZAO070 | Error | BulkInsert method must take exactly one collection parameter | [ZAO070](ZAO070.md) |
| ZAO071 | Error | BulkInsert SQL must contain exactly one VALUES tuple | [ZAO071](ZAO071.md) |
| ZAO072 | Error | BulkInsert placeholder doesn't match any TRow property | [ZAO072](ZAO072.md) |
| ZAO073 | Error | BulkInsert return type must be `Task<int>` or `Task<IReadOnlyList<TIdentity>>` | [ZAO073](ZAO073.md) |
| ZAO074 | Info | `CommandKind.BulkInsert` is ignored on this attribute | [ZAO074](ZAO074.md) |
| ZAO080 | Warning | At most one `IAsyncDbTransaction` parameter | [ZAO080](ZAO080.md) |
| ZAO081 | Error | Containing type must be partial (nested repository) | [ZAO081](ZAO081.md) |

## Code-range conventions

- `ZAO001`–`ZAO019` — hard errors and warnings about user-authored code shape.
- `ZAO020`–`ZAO039` — informational notices about deferred features, plus tuple/SQL-statement arity mismatches (`ZAO032`, `ZAO033`).
- `ZAO040`–`ZAO052` — materialization, binding and composite-type construction failures.
- `ZAO060`–`ZAO068` — `[StoredProcedure]` and `[Param]` guardrails. `ZAO060` is reserved: registered for catalog completeness but never emitted (C# already rejects the case it targets via CS1988).
- `ZAO070`–`ZAO074` — `BulkInsert` shape diagnostics.
- `ZAO080`–`ZAO081` — transaction-parameter and nested-repository partial-type guardrails.

Diagnostic IDs are stable across releases; new diagnostics use the next free slot in the appropriate range. Retired IDs (for example `ZAO021`, removed in v0.3 Phase B.5) are not reused and are dropped from this table once removed from `DiagnosticDescriptors.cs`.

## Release tracking

A new diagnostic goes into `src/ZeroAlloc.ORM.Generator/AnalyzerReleases.Unshipped.md`. The build fails if a descriptor has no entry there or in `AnalyzerReleases.Shipped.md`.

`AnalyzerReleases.Shipped.md` records which release each rule first shipped in, and which releases removed a rule. Once a rule has shipped, changing its severity or category, or removing it, has to be declared under `### Changed Rules` or `### Removed Rules` in the Unshipped file. A silent change to a shipped rule fails the build.

On release, the Unshipped rows move into a `## Release x.y.z` section of the Shipped file. The move is automated: when release-please opens or updates the release PR, the `ship-release-tracking` job in `.github/workflows/release-please.yml` runs the org's shared [`ship-release-tracking.py`](https://github.com/ZeroAlloc-Net/.github/blob/main/scripts/ship-release-tracking.py) with that release's version on the branch. The script moves the analyzer rules and the `PublicAPI.Unshipped.txt` entries of every package in one commit. **Release checklist:** before merging a release PR, check that it contains that commit. If it doesn't, run `python3 <path to ZeroAlloc-Net/.github>/scripts/ship-release-tracking.py <version>` from the root of the release branch and push the result. The `release-tracking` job in `.github/workflows/ci.yml` fails a release PR while any Unshipped entry remains; the same script with `--check` runs that check locally.
