# Multi-result-set queries (head + lines pattern)

The classic "load an order with its lines" query is a 1+N round-trip in most code
bases: one SELECT for the order row, then N SELECTs for each line. ZeroAlloc.ORM
collapses that to a **single round-trip** by recognising tuple return types and
SQL containing multiple statements separated by `;`.

## The pattern

Declare a partial method whose return type is a tuple. Each tuple element maps to
one result set in the SQL:

```csharp
using System.Collections.Generic;
using System.Data.Async;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.ORM;

public sealed record OrderRow(int Id, int CustomerId, decimal Total);
public sealed record OrderLineRow(int Id, int OrderId, string Sku, int Qty);

public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    [Query("""
        SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id;
        SELECT Id, OrderId, Sku, Qty FROM OrderLines WHERE OrderId = @id;
        """)]
    public partial Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?> GetWithLinesAsync(
        int id,
        CancellationToken ct);
}
```

A call to `repo.GetWithLinesAsync(42, ct)` issues one database round-trip and
returns either `null` (no order with that id) or a tuple containing the head row
plus the list of lines.

Tuple element shapes recognised by the generator:

- **Scalar** — `int`, `string`, value-objects, enums, ... — the result set's
  first column of its first row is read. SQL example: `SELECT COUNT(*) FROM ...`.
- **Row** — a positional record (`record OrderRow(int Id, ...)`) or a class with
  a single multi-arg ctor (`DomainEntity` shape). The first row of the result
  set is materialised; subsequent rows are ignored.
- **List** — `List<T>`, `IReadOnlyList<T>`, `IList<T>`, `IEnumerable<T>`,
  `ICollection<T>`, `IReadOnlyCollection<T>` — every row of the result set is
  materialised into the accumulator.

A 3-element tuple combining all three kinds:

```csharp
[Query("""
    SELECT COUNT(*) FROM Orders;
    SELECT Id, CustomerId, Total FROM Orders ORDER BY Id LIMIT 1;
    SELECT Id, CustomerId, Total FROM Orders ORDER BY Id;
    """)]
public partial Task<(int Count, OrderRow First, IReadOnlyList<OrderRow> All)?> GetCountFirstAllAsync(
    CancellationToken ct);
```

> Note: `LIMIT 1` is SQLite / Postgres / MySQL syntax. On SQL Server use `SELECT TOP 1 ...` instead.

## Choosing the dispatch path: `BatchMode`

The generator emits two dispatch paths and picks one based on `[Query(Batch = ...)]`:

| Mode               | Behavior                                                                                  |
| ------------------ | ----------------------------------------------------------------------------------------- |
| `BatchMode.Auto`   | Default. At runtime, branches on `connection.CanCreateBatch`: provider supports `IAsyncDbBatch` -> use it; otherwise fall back to a single command with `;`-joined SQL and `NextResultAsync`. |
| `BatchMode.Always` | Always use `IAsyncDbBatch`. Fails at runtime if the provider does not support batches.    |
| `BatchMode.Never`  | Always use the single-command `;`-joined fallback with `NextResultAsync`.                 |

- **Prefer `Auto`** unless you know the provider intimately. The `;`-joined path
  is universally supported but goes through one logical command boundary;
  `IAsyncDbBatch` (when available) gives the provider freedom to pipeline.
- **`Never`** is useful when targeting providers whose `IAsyncDbBatch` shape is
  brittle (older ADO.NET drivers, in-memory test doubles) or when a
  query-rewriting middleware sits in front of the connection.
- **`Always`** is rarely the right choice — it removes the safety net and fails
  fast with `NotSupportedException` from the connection (`CreateBatch()` on a
  provider without `IAsyncDbBatch`) when the underlying driver does not implement
  batching.

## Arity must match: ZAO032 / ZAO033

The number of `;`-separated SQL statements must exactly match the tuple arity.
The generator emits compile-time diagnostics when they don't:

- **ZAO032** — tuple has more elements than `;`-statements (you forgot a SELECT).
- **ZAO033** — tuple has fewer elements than `;`-statements (you have a stray
  SELECT that no tuple field will consume).

Both fire at build time, so the wrong shape never ships.

## Procedure-driven head + lines

The same tuple shape works for stored procedures — wrap the multi-result
procedure with `[StoredProcedure]` instead of `[Query]`. See Recipe 4 in
[`stored-procedures.md`](stored-procedures.md) for the procedure-side variant.

## When NOT to use this pattern

- **One result set, list-shaped** — use `Task<List<T>>` instead of
  `Task<(List<T>)>`. The single-result list emit is simpler and avoids the
  tuple wrapping.
- **Huge result sets** — see [`streaming.md`](streaming.md). Multi-result-set
  materialises the entire `Lines` collection into memory; if `Lines` can be
  millions of rows, prefer two methods (a single-row `Head` fetch + an
  `IAsyncEnumerable<Line>` stream) so consumers stay alloc-bounded.
- **Conditional shape** — if the second result set might be absent depending on
  data, the tuple shape with a non-nullable tuple field will raise a
  materialisation exception. Model the optionality with a nullable tuple
  element or split into two queries.
- **Single-value writes / scalar aggregates** — use `[Command]` instead. See
  [`commands.md`](commands.md) for INSERT / UPDATE / DELETE rows-affected,
  scalar aggregates (`COUNT`, `SUM`), and identity (`RETURNING`) capture.
