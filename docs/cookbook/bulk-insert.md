# Bulk insert — `CommandKind.BulkInsert`

When you need to insert *several* rows in a single call without paying for N
round-trips. The generator emits a chunked multi-row
`INSERT ... VALUES (...), (...), ...` pipeline with optional
`RETURNING`-based identity capture.

`CommandKind.BulkInsert` lives on `[Command]` alongside `NonQuery` / `Scalar`
/ `Identity` — same attribute surface, different emit shape. The method
takes a single `IReadOnlyList<TRow>` (or `IEnumerable<TRow>` /
`IReadOnlyCollection<TRow>` / array) of rows to insert; the generator
unrolls each row's properties into the `VALUES (...)` tuple at codegen.

## When to reach for BulkInsert

- **Small-to-medium batches (5–500 rows)** — the sweet spot. The chunked
  multi-row VALUES syntax eliminates per-row round-trips while keeping the
  SQL portable across Sqlite / Postgres / SQL Server / MySQL.
- **Identity capture across N rows** — `RETURNING <col>` (Postgres,
  Sqlite ≥ 3.35, SQL Server `OUTPUT INSERTED.<col>` with an adapted query)
  returns a list of generated IDs in input order.
- **Not a fit for 10k+ row workloads** — provider-native bulk paths
  (`SqlBulkCopy`, Postgres `COPY`, MySQL `LOAD DATA INFILE`) are 10–100x
  faster at that scale. Those wire-protocol paths are deferred to a future
  ZA.ORM release.

If you only need a single-row insert, stay on `CommandKind.NonQuery` or
`CommandKind.Identity` — see [`commands.md`](commands.md).

## Recipe 1 — Rows-affected sum

The default shape. The generator emits one `ExecuteNonQueryAsync` per chunk
and surfaces the **sum of rows-affected counts across all chunks** as the
return value:

```csharp
using System.Collections.Generic;
using System.Data.Async;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.ORM;

public sealed record OrderRow(int CustomerId, decimal Total);

public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    [Command(
        "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)",
        Kind = CommandKind.BulkInsert)]
    public partial Task<int> InsertOrdersAsync(
        IReadOnlyList<OrderRow> orders,
        CancellationToken ct);
}
```

```csharp
var orders = new List<OrderRow>
{
    new(42, 10.00m),
    new(42, 11.00m),
    new(43, 99.99m),
};

var inserted = await repo.InsertOrdersAsync(orders, ct).ConfigureAwait(false);
// inserted == 3
```

`@CustomerId` and `@Total` are not method parameters — they're
**`TRow` property names**. The generator matches each `@placeholder` in the
SQL against a property on `OrderRow` (case-insensitive) and emits the
corresponding `reader.GetXxx` / parameter `Value =` plumbing at codegen.

Chunk size is baked at codegen as `900 / placeholderCount` rows per chunk.
For this 2-placeholder shape, that's 450 rows per chunk. See the
[chunking semantics](#chunking-semantics) section for the why.

## Recipe 2 — Identity capture via `RETURNING`

Return `Task<IReadOnlyList<TIdentity>>` and include a `RETURNING <col>`
clause in the SQL. The generator emits one `ExecuteReaderAsync` per chunk,
reads the returned column for every row, and concatenates the results into
a single list **in input order**:

```csharp
[Command(
    "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total) RETURNING Id",
    Kind = CommandKind.BulkInsert)]
public partial Task<IReadOnlyList<int>> InsertOrdersAsync(
    IReadOnlyList<OrderRow> orders,
    CancellationToken ct);
```

```csharp
var ids = await repo.InsertOrdersAsync(orders, ct).ConfigureAwait(false);
// ids[0] is the auto-generated Id for orders[0], etc.
```

`TIdentity` must be one of:

- `int` / `long` / `Guid` — bound directly via the matching
  `reader.GetXxx(0)` accessor.
- A `[ValueObject]` wrapping one of those — bound via the VO's primary
  constructor (`ConventionKind.SingleArgCtor`) — see Recipe 3.

Anything else (`string`, `DateTime`, multi-column return shapes, ...)
trips [ZAO073](../diagnostics/ZAO073.md) at compile time.

The list is materialised once across all chunks. ZA.ORM does **not** stream
the identities back — for that scale, consider whether `BulkInsert` is the
right tool (see [When to reach for BulkInsert](#when-to-reach-for-bulkinsert)).

## Recipe 3 — TRow with value objects

Both `TRow`'s properties and `TIdentity` can be `[ValueObject]`-wrapped
primitives. The generator unwraps the VO via its convention (single-arg
ctor or `static From`) when binding parameters and rewraps on the read
side:

```csharp
[ValueObject(typeof(int))]
public readonly partial struct CustomerId
{
    public int Value { get; }
    public CustomerId(int v) => Value = v;
}

[ValueObject(typeof(int))]
public readonly partial struct OrderId
{
    public int Value { get; }
    public OrderId(int v) => Value = v;
}

public sealed record OrderRow(CustomerId CustomerId, decimal Total);

[Command(
    "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total) RETURNING Id",
    Kind = CommandKind.BulkInsert)]
public partial Task<IReadOnlyList<OrderId>> InsertOrdersAsync(
    IReadOnlyList<OrderRow> orders,
    CancellationToken ct);
```

On the parameter side, `CustomerId` is unwrapped to `int` (via
`customer.Value`) when binding `@CustomerId`. On the read side, the `int`
from `RETURNING Id` is wrapped back into `OrderId` via its primary
constructor. No reflection, no per-row delegate — same emit shape as a
bare-`int` insert.

## Chunking semantics

Each chunk is its own `ExecuteNonQueryAsync` / `ExecuteReaderAsync` call.
That has two consequences worth thinking about up front:

1. **Atomicity is per chunk, not across chunks.** If chunk 3 of 5 fails,
   chunks 1–2 are already committed (or visible, on autocommit
   connections). If you need all-or-nothing across all chunks, wrap the
   call in an explicit transaction:

   ```csharp
   await using var tx = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
   try
   {
       var ids = await repo.InsertOrdersAsync(orders, ct).ConfigureAwait(false);
       await tx.CommitAsync(ct).ConfigureAwait(false);
       return ids;
   }
   catch
   {
       await tx.RollbackAsync(ct).ConfigureAwait(false);
       throw;
   }
   ```

   The `connection` parameter the partial method captures must be the same
   connection that started the transaction. ZA.ORM's `IAsyncDbConnection`
   honors the active transaction implicitly — no explicit pass-through
   needed (same convention as every other `[Command]` / `[Query]` method).

2. **Chunk size is baked at codegen.** The budget is
   `900 / placeholderCount` rows per chunk, rounded down, minimum 1.
   That's a static decision the generator makes from the SQL string — it
   does not adapt at runtime. For the typical schemas this matters for:

   | Placeholders per row | Rows per chunk |
   | -------------------: | -------------: |
   | 2                    | 450            |
   | 5                    | 180            |
   | 10                   |  90            |
   | 20                   |  45            |
   | 50                   |  18            |

   A 900-parameter budget keeps the emit under Sqlite's hard 999-parameter
   cap (the lowest of the four providers). Larger-cap providers get
   conservative headroom for free; see the table below.

## Per-provider notes

| Provider     | Per-statement parameter cap                      | Chunk size (2-col) | Chunk size (10-col) | Integration suite |
| ------------ | ------------------------------------------------ | ----------------: | ------------------: | ----------------- |
| Sqlite       | 999 (hard, `SQLITE_MAX_VARIABLE_NUMBER`)         |  450              |  90                 | covered in v1.3   |
| PostgreSQL   | 65535 (`int16` wire-protocol parameter index)    |  450              |  90                 | covered in v1.3   |
| SQL Server   | 2100                                             |  450              |  90                 | snapshot-only     |
| MySQL        | bounded by `max_allowed_packet`, not parameter count | 450           |  90                 | snapshot-only     |

The 900-parameter budget is conservative on every provider except Sqlite,
where it deliberately stays a hair under the 999 cap. A provider-aware
chunk size (read the cap from the connection, emit accordingly) is a
backlog item — raise it if you measure the extra round-trips on a
high-cap provider hurting throughput.

See [`provider-quirks.md`](provider-quirks.md) for the consolidated
per-provider parameter-cap notes.

## Diagnostics

| Code                                       | Severity | Trigger                                                                                                       |
| ------------------------------------------ | -------- | ------------------------------------------------------------------------------------------------------------- |
| [ZAO070](../diagnostics/ZAO070.md)         | Error    | Method missing a single collection parameter (`IReadOnlyList<TRow>` / `IEnumerable<TRow>` / array).            |
| [ZAO071](../diagnostics/ZAO071.md)         | Error    | SQL has zero or multiple `VALUES (...)` tuples — `BulkInsert` requires exactly one tuple to template against. |
| [ZAO072](../diagnostics/ZAO072.md)         | Error    | A `@placeholder` in the SQL doesn't match any `TRow` property (case-insensitive).                              |
| [ZAO073](../diagnostics/ZAO073.md)         | Error    | Return type isn't `Task<int>` or `Task<IReadOnlyList<TIdentity>>` (or VO-wrapped identity).                    |
| [ZAO074](../diagnostics/ZAO074.md)         | Info     | `Kind = CommandKind.BulkInsert` on `[Query]` or `[StoredProcedure]` is ignored — the kind applies to `[Command]` only. |

## Related cookbook recipes

- [`commands.md`](commands.md) — single-row `[Command]` reference
  (`NonQuery` / `Scalar` / `Identity`).
- [`provider-quirks.md`](provider-quirks.md) — per-provider parameter caps,
  identity idioms, identifier folding.
- [`multi-result-set.md`](multi-result-set.md) — when one statement
  produces multiple result sets (the dual problem to BulkInsert's
  one-shape-many-rows).
- [`flat-row.md`](flat-row.md) — single-row reads paired with `Identity`
  inserts (the canonical insert-and-fetch flow for one row).
