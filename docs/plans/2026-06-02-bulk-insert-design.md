# Design — `CommandKind.BulkInsert` for ZeroAlloc.ORM

**Status:** approved 2026-06-02
**Target version:** 1.3.0
**Author:** brainstorming session
**Implementation plan:** to be created next via `superpowers:writing-plans`

## Context

ZA.ORM's existing `[Command]` shapes (`NonQuery`, `Scalar`, `Identity`) all assume single-row execution. Adopters who need to insert N rows in one HTTP request currently call the single-row method in a loop — one network round-trip per row. EF Core's `SaveChanges` solves the same problem with a multi-row `INSERT … VALUES (…), (…), …` statement (default ~42 rows per batch, chunked if larger); the ZA.Templates EF→ZA.ORM swap surfaced this as the one architectural gap where EF still has an edge on the write path.

This design adds a fourth `CommandKind` value, `BulkInsert`, that emits the multi-row VALUES pattern with automatic chunking to stay within provider parameter-count limits. Out of scope: provider-native bulk paths (`SqlBulkCopy` / `COPY` / `MySqlBulkCopy` / etc.) — they're a different shape entirely (no SQL template, provider-specific stream APIs, weak identity-capture support) and warrant a separate design when adopters target 10k+ row bulk-load workloads.

## Architecture

`CommandKind` gains a fourth value, `BulkInsert`. Generator classifier adds a new `EmitShape.BulkInsertCommand` triggered by `[Command(Kind = CommandKind.BulkInsert)]`. The shape requires:

- Exactly one collection parameter typed `IReadOnlyList<TRow>` / `IList<TRow>` / `IEnumerable<TRow>` (plus optional `CancellationToken`)
- TRow has a public property matching every `@placeholder` in the VALUES tuple (case-insensitive name match)
- Return type is one of: `Task<int>` (rows-affected sum across chunks) or `Task<IReadOnlyList<TIdentity>>` (concatenated identity values across chunks; SQL must include `RETURNING <id-col>` / `OUTPUT INSERTED.<col>`)

Emit produces a chunk-aware open / build-SQL / bind-params / execute / close pipeline. Chunk size = `900 / placeholderCount` (the 900 budget stays safely under Sqlite's 999-parameter cap; the per-row placeholder count is known at codegen time). Materializes the input enumerable once into a `List<T>` buffer when the parameter isn't already an `IReadOnlyList<T>`.

Chunked execution is not atomic across chunks — a constraint violation in chunk 5 of 10 commits chunks 1–4. Adopters who need all-or-nothing semantics wrap the call in their own `DbTransaction`. Cookbook recipe documents the pattern explicitly.

A v0.3-era `IAsyncDbBatch` infrastructure already exists in the codebase for **multi-statement** batching. We re-use `IAsyncDbCommand` (one statement with N rows of params), not `IAsyncDbBatch` (N statements). Documentation calls out the distinction.

## API surface

**`ZeroAlloc.ORM.Abstractions/CommandKind.cs` — new enum value:**

```csharp
public enum CommandKind
{
    NonQuery = 0,
    Scalar = 1,
    Identity = 2,
    BulkInsert = 3,  // NEW
}
```

Additive — no breakage. `PublicAPI.Shipped.txt` gets one new line.

**Method signature contract — rows-affected variant:**

```csharp
[Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)",
         Kind = CommandKind.BulkInsert)]
public partial Task<int> InsertOrdersAsync(
    IReadOnlyList<OrderRow> orders,
    CancellationToken ct);
```

**Identity-capture variant — SQL must include `RETURNING <id>`:**

```csharp
[Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total) RETURNING Id",
         Kind = CommandKind.BulkInsert)]
public partial Task<IReadOnlyList<int>> InsertOrdersAsync(
    IReadOnlyList<OrderRow> orders,
    CancellationToken ct);
```

**Accepted collection parameter types** (one per method, exactly):
- `IReadOnlyList<TRow>` — preferred; generator uses `.Count` and indexed access directly
- `IList<TRow>` — same Count behavior; mutable but the generator treats it as read
- `IEnumerable<TRow>` — generator emits a buffered adapter (`new List<TRow>(orders)`) at method entry so the rest of the pipeline can use indexed access. One allocation cost.

**Accepted return types:**
- `Task<int>` — rows-affected sum across chunks
- `Task<IReadOnlyList<TIdentity>>` where `TIdentity` ∈ {`int`, `long`, `Guid`, `[ValueObject]` wrapping one of those}. Matches `CommandKind.Identity`'s convention.

**TRow constraints** (compile-time enforced by diagnostics, see below):
- Class, record (sealed or not), or struct
- Public property for every `@placeholder` in the VALUES tuple; case-insensitive name match
- Property values go through the existing `ConventionDiscovery` path — primitive types, `[ValueObject]` factories, `[StoreAsString]` enums, etc., all work identically to single-row `[Command]` parameter binding

**Out of scope for v1.3:**
- Returning `Task<IReadOnlyList<TRow>>` (full materialized rows with assigned IDs) — useful but adds a row-materialization layer; defer to v1.4 or later
- Multi-statement bulk (parent + child INSERTs in one call) — different shape; use existing `IAsyncDbBatch` infrastructure
- Provider-native bulk paths (`SqlBulkCopy` etc.) — separate design when 10k+ row workloads become a real adopter ask
- `CommandKind.BulkInsert` on `[StoredProcedure]` or `[Query]` — diagnostic rejects with an Info-level message

## Emit pipeline

Generator parses the user's SQL string to find the single `VALUES (...)` tuple, extracts the placeholder names from it, and bakes the list into the emit. Two-place lookup happens at codegen time:
1. SQL parser → `[CustomerId, Total]` placeholder list
2. TRow type symbol → property table (`CustomerId: int`, `Total: decimal`, ...)
3. Each placeholder name matches against the property table (case-insensitive); the matched property's getter is what the parameter binding emit reads

**Generated method shape (rows-affected variant):**

```csharp
public partial async Task<int> InsertOrdersAsync(
    IReadOnlyList<OrderRow> orders,
    CancellationToken ct)
{
    if (orders.Count == 0) return 0;

    var __conn = connection;
    var __openedHere = __conn.State != ConnectionState.Open;
    if (__openedHere) await __conn.OpenAsync(ct).ConfigureAwait(false);
    try
    {
        const int __chunkSize = 450;  // 900 / 2 placeholders, baked at codegen
        var __totalAffected = 0;
        var __offset = 0;
        var __remaining = orders.Count;

        while (__remaining > 0)
        {
            var __thisChunk = __remaining < __chunkSize ? __remaining : __chunkSize;
            await using var __cmd = __conn.CreateCommand();

            // Build the multi-row VALUES SQL for this chunk
            var __sb = new global::System.Text.StringBuilder("INSERT INTO Orders (CustomerId, Total) VALUES ");
            for (var __i = 0; __i < __thisChunk; __i++)
            {
                if (__i > 0) __sb.Append(", ");
                __sb.Append("(@CustomerId_").Append(__i).Append(", @Total_").Append(__i).Append(')');
            }
            __cmd.CommandText = __sb.ToString();

            // Bind params for this chunk
            for (var __i = 0; __i < __thisChunk; __i++)
            {
                var __row = orders[__offset + __i];
                var __p0 = __cmd.CreateParameter();
                __p0.ParameterName = "@CustomerId_" + __i;
                __p0.Value = __row.CustomerId;
                __cmd.Parameters.Add(__p0);
                var __p1 = __cmd.CreateParameter();
                __p1.ParameterName = "@Total_" + __i;
                __p1.Value = __row.Total;
                __cmd.Parameters.Add(__p1);
            }

            __totalAffected += await __cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            __offset += __thisChunk;
            __remaining -= __thisChunk;
        }
        return __totalAffected;
    }
    finally
    {
        if (__openedHere) await __conn.CloseAsync().ConfigureAwait(false);
    }
}
```

**Identity-capture variant** swaps `ExecuteNonQueryAsync` for `ExecuteReaderAsync` + `while ReadAsync` drain into a `List<TIdentity>`, returns the concatenated list across chunks.

**`IEnumerable<T>` adapter** when the parameter isn't `IReadOnlyList<T>`:

```csharp
var __rows = orders is IReadOnlyList<OrderRow> __irol ? __irol : new List<OrderRow>(orders);
```

Inserted at method entry; rest of the pipeline uses `__rows`.

**Connection lifecycle** — standard ref-counted open-on-execute / close-on-finally **around the whole chunk loop** (not per-chunk). Keeps the connection on the slot for the duration of the bulk operation.

**Chunk size baked at codegen** — `900 / placeholderCount`. For a 2-column row: 450; 4-column: 225; 10-column: 90. The 900 budget keeps us safely under Sqlite's 999-parameter cap and is well under SQL Server's 2100; Postgres at ~32k has plenty of headroom either way.

## Diagnostics

Five new compile-time codes (actual ZAO0xx numbers assigned during implementation by scanning the existing range in `src/ZeroAlloc.ORM.Generator/Diagnostics/DiagnosticDescriptors.cs`):

| ID | Severity | Trigger | Message |
|---|---|---|---|
| ZAO0NN | Error | `[Command(Kind = CommandKind.BulkInsert)]` method has no `IEnumerable<T>`-shaped collection parameter, or has more than one collection parameter | "BulkInsert method must have exactly one IEnumerable<T>-shaped collection parameter; saw {n}" |
| ZAO0NN | Error | User's SQL doesn't contain exactly one `VALUES (placeholder, ...)` tuple, or the parser can't extract the placeholder list | "BulkInsert SQL must contain exactly one VALUES (placeholder, ...) tuple; saw {n}" |
| ZAO0NN | Error | A placeholder in the VALUES tuple has no matching public property on TRow | "BulkInsert: TRow '{type}' has no public property matching placeholder '@{name}'" |
| ZAO0NN | Error | Method's return type isn't `Task<int>` or `Task<IReadOnlyList<TIdentity>>` for an allowed `TIdentity` | "BulkInsert return type must be Task<int> (rows-affected) or Task<IReadOnlyList<T>> (identity capture); saw {actual}" |
| ZAO0NN | Info | `CommandKind.BulkInsert` used with `[StoredProcedure]` or `[Query]` instead of `[Command]` | "BulkInsert kind is only valid on [Command]; ignored on {actual}" |

## Testing strategy

**Snapshot tests** — `tests/ZeroAlloc.ORM.Generator.Tests/Emit/BulkInsertTests.cs`:

- `BulkInsert_Task_int_emits_chunked_NonQuery_pipeline.verified.cs` (4 cols, 450 chunk size, standard shape)
- `BulkInsert_Task_IReadOnlyList_int_emits_chunked_ExecuteReader_with_RETURNING.verified.cs`
- `BulkInsert_with_IEnumerable_parameter_emits_buffered_adapter.verified.cs`
- `BulkInsert_with_ValueObject_identity_emits_factory_wrap.verified.cs`
- `BulkInsert_chunk_size_scales_with_placeholder_count.verified.cs` (10-col table → chunk size 90)

Plus `tests/ZeroAlloc.ORM.Generator.Tests/Diagnostics/BulkInsertDiagnosticsTests.cs` with one test per ZAO code.

**Integration tests** — `tests/ZeroAlloc.ORM.Integration.Tests/BulkInsertTests.cs` (Sqlite) + `Postgres/PostgresBulkInsertTests.cs`:

- Insert 5 rows → rows-affected == 5; all rows queryable via subsequent SELECT
- Insert 5 rows with `RETURNING Id` → identity list length == 5; IDs roundtrip via SELECT
- **Insert 1000 rows on Sqlite** — forces chunking (well above the 999-param cap for a 2-col table at 450/chunk = 3 chunks for 1000 rows). Verify all rows go in, returns correct sum / identity list.
- Empty collection → returns 0 / empty list with zero DB round-trips (verified via reader-state probe or call counter)
- TRow with a `[ValueObject]` typed-ID column → unwrap + bind correctly
- TRow with a `[StoreAsString]` enum column → bound as string

**Provider scope:**
- Sqlite + Postgres: full integration coverage (the two providers ZA.ORM ships test infrastructure for).
- SQL Server + MySQL: "supported in principle — multi-row VALUES is standard SQL; bring your own integration tests until upstream coverage lands." Document in the cookbook recipe under "Provider quirks."

## Cookbook docs

**`docs/cookbook/bulk-insert.md`** (new, primary recipe):
- "When to reach for BulkInsert" — small/medium batches (5–500 rows). For 10k+ row workloads, point at the future provider-native bulk path (link to backlog entry).
- Worked example: rows-affected variant using the same `Orders` table the other recipes use.
- Identity-capture variant with `RETURNING Id`.
- Chunking semantics — atomic per-chunk, not across chunks. "If you need all-or-nothing, wrap the call in your own `IDbTransaction`" + concrete snippet.
- Per-provider notes:
  - Sqlite: 999-param cap; chunk size auto-scaled
  - Postgres: 32k-param cap; rarely an issue
  - SQL Server: 2100-param cap; works in principle; integration coverage TBD
  - MySQL: standard multi-row VALUES works; integration coverage TBD
- Cross-link to `provider-quirks.md` and `commands.md`.

**`docs/cookbook/provider-quirks.md`** — one paragraph added under each provider section noting per-provider parameter-count limits and how they interact with chunk size.

**`docs/cookbook/commands.md`** — brief note in the existing `[Command]` overview cross-linking to the new bulk-insert recipe; one row added to the `CommandKind` shape table for `BulkInsert`.

## Carry-forwards (deferred to v1.4 or later)

- **Returning full row records with assigned IDs** — `Task<IReadOnlyList<TRow>>`. Adds a row-materialization layer (need to read every column of every RETURNING-d row, not just identity). Worth doing once adopter demand surfaces.
- **Provider-native bulk paths** (`SqlBulkCopy` for SQL Server, `NpgsqlBinaryImporter` aka `COPY` for Postgres, `MySqlBulkCopy` for MySQL) — separate design. Different API shape (no SQL template; binary-stream API), different perf characteristics (10–100× faster than multi-row VALUES for 10k+ rows), different limitations (identity capture is hard or impossible). Track as a separate backlog entry.
- **Multi-statement INSERT batching** — e.g. parent + child rows in one call. Different shape; use the existing `IAsyncDbBatch` infrastructure.
- **Conditional INSERT** (`INSERT ... ON CONFLICT` / `MERGE`) — provider-specific syntax; out of scope here.

## Backward compatibility

100% additive. `CommandKind.BulkInsert = 3` adds one new enum value; existing `NonQuery` / `Scalar` / `Identity` callers continue to work identically. PublicAPI.Shipped.txt grows by one line. No emit changes to existing shapes; the new shape is dispatched by `Kind == BulkInsert`.

The chunking behavior is the only place where a user-observable semantic could surprise: a previously-failed-with-too-many-params single-statement INSERT (under a hypothetical user who manually wrote a thousand `(?, ?)` tuples) now succeeds via chunking. We don't anticipate any user doing that today — single-statement multi-row INSERTs of thousands of rows aren't an idiom anyone reaches for.

## Surfaced by

ZA.Templates EF Core → ZA.ORM swap ([ZeroAlloc.Templates PR #152](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/152)). The swap's bench numbers showed za-clean's POST /orders losing ~700μs relative to EF on Postgres for multi-line orders — root cause was EF's batched INSERT emitting one statement vs ZA.ORM's per-line round-trip. This design closes that gap.
