# ZeroAlloc.ORM diagnostic reference

The generator emits compile-time diagnostics with stable `ZAO0NN` codes. Each descriptor's `helpLinkUri` points back into this directory, so clicking the link in your IDE lands on the relevant page.

## v0.1 shipped diagnostics

| Code | Severity | Title |
|------|----------|-------|
| [ZAO001](ZAO001.md) | Error | Annotated method must be partial |
| [ZAO002](ZAO002.md) | Error | Unsupported return type |
| [ZAO003](ZAO003.md) | Error | No IAsyncDbConnection found on containing type |
| [ZAO004](ZAO004.md) | Error | Containing type must be partial |
| [ZAO005](ZAO005.md) | Error | Multiple ORM attributes on one method |
| [ZAO006](ZAO006.md) | Warning | Method has multiple CancellationToken parameters |
| [ZAO007](ZAO007.md) | Error | IAsyncEnumerable&lt;T&gt; return without [EnumeratorCancellation] |
| [ZAO008](ZAO008.md) | Error | Multi-statement SQL with single-result return type |
| [ZAO009](ZAO009.md) | Warning | Redundant async keyword on generated partial |
| [ZAO020](ZAO020.md) | Info | [Query(FromResource)] not yet implemented in v0.1 |
| [ZAO021](ZAO021.md) | Info | [Query(Batch = ...)] non-Auto value not yet implemented in v0.1 |

## Code-range conventions

- `ZAO001`–`ZAO019` — hard errors and warnings about user-authored code shape.
- `ZAO020`–`ZAO039` — informational notices about features deferred to future milestones.

Diagnostic IDs are stable across releases; new diagnostics use the next free slot in the appropriate range.
