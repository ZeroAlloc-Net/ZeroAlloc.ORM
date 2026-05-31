# Streaming with `IAsyncEnumerable<T>`

For result sets that don't fit comfortably in memory — large reports, ETL feeds,
periodic full-table scans — ZeroAlloc.ORM supports the `IAsyncEnumerable<T>`
return shape. Each row materialises lazily; the consumer's `await foreach` loop
controls how much of the result set is actually loaded.

## The pattern

```csharp
using System.Collections.Generic;
using System.Data.Async;
using System.Runtime.CompilerServices;
using System.Threading;
using ZeroAlloc.ORM;

public sealed record OrderRow(int Id, int CustomerId, decimal Total);

public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Id, CustomerId, Total FROM Orders ORDER BY Id")]
    public partial IAsyncEnumerable<OrderRow> StreamAllAsync(
        [EnumeratorCancellation] CancellationToken ct);
}
```

Consume with `await foreach`:

```csharp
await foreach (var order in repo.StreamAllAsync(ct).ConfigureAwait(false))
{
    await ProcessAsync(order).ConfigureAwait(false);
}
```

## `[EnumeratorCancellation]` is required

The cancellation token parameter on a streaming method MUST carry the
`[EnumeratorCancellation]` attribute. Without it, the generator emits the
**ZAO007** diagnostic at build time. The attribute lets the compiler thread the
caller's `WithCancellation(ct)` token through the iterator, so cancellation
flows correctly into the underlying `ReadAsync` calls.

```csharp
// ZAO007 fires here:
public partial IAsyncEnumerable<OrderRow> StreamAllAsync(CancellationToken ct);

// ZAO007 is satisfied:
public partial IAsyncEnumerable<OrderRow> StreamAllAsync(
    [EnumeratorCancellation] CancellationToken ct);
```

## Lifecycle: when does the connection open?

The generated iterator does NOT touch the connection until the consumer calls
`MoveNextAsync` for the first time (typically via the first `await foreach`
iteration). It opens the connection (if not already open), creates the command,
executes the reader, and yields rows one at a time.

When the consumer exits the loop — normally OR by `break` / exception — the
iterator's `DisposeAsync` runs through a `try`/`finally`:

1. Disposes the data reader.
2. Disposes the command.
3. Closes the connection **if and only if** the iterator opened it.

The "did we open it?" check matters: if the caller passed in an already-open
connection (e.g. shared across multiple repository calls in a transaction), the
streaming method leaves it open for the caller to manage.

## Early-break safety

```csharp
await foreach (var order in repo.StreamAllAsync(ct).ConfigureAwait(false))
{
    if (order.Total > 1_000m)
    {
        await NotifyAsync(order).ConfigureAwait(false);
        break;  // safe — DisposeAsync runs, reader + connection clean up
    }
}
```

Breaking out of `await foreach` triggers `IAsyncEnumerator.DisposeAsync`, which
runs the iterator's `finally` block. The reader, command, and connection (when
owned) all clean up deterministically. No leaks, no half-open readers.

## When to prefer streaming vs. `Task<List<T>>`

| Scenario                                      | Prefer                                          |
| --------------------------------------------- | ----------------------------------------------- |
| Result set fits comfortably in RAM (~few thousand rows of small records) | `Task<List<T>>` — simpler call sites, one round-trip, single allocation for the list. |
| Result set is unbounded or large (ETL, reports, full-table scans) | `IAsyncEnumerable<T>` — alloc-bounded, lets the consumer process row-by-row. |
| Caller wants to stop early once a condition is met | `IAsyncEnumerable<T>` — break + DisposeAsync stops the query mid-stream. |
| Caller passes the result into LINQ / batching helpers | `Task<List<T>>` — easier interop. (Or stream + adapt to `IEnumerable<T>` chunk-wise.) |

The trade-off is memory pressure vs. per-row overhead. For small N (say < a few
thousand small rows), `Task<List<T>>` is usually the right shape — the list
allocation is dwarfed by the row materialisations, and the consumer code is
simpler. For large N or unknown N, streaming keeps the working set bounded.

## Cancellation propagation

The `[EnumeratorCancellation]`-marked token is the one observed inside
`ReadAsync` and `NextResultAsync`. Cancelling it mid-stream surfaces as
`OperationCanceledException` from the awaiting `MoveNextAsync` — which `await
foreach` rethrows in the caller. The iterator's `finally` still runs, so the
reader and (owned) connection close cleanly even on cancellation.

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await foreach (var row in repo.StreamAllAsync(cts.Token).ConfigureAwait(false))
{
    // After 30s the next MoveNextAsync throws OperationCanceledException.
    // The iterator's finally still cleans up.
}
```
