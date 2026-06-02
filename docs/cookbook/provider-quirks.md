# Provider quirks

ZeroAlloc.ORM is provider-agnostic at the generator level — the emit
references `IAsyncDbConnection` from [AdoNet.Async](https://github.com/MarcelRoozekrans/AdoNet.Async),
not any concrete provider type. The generator does not branch on provider;
it does not auto-rewrite SQL; it does not normalise identifier casing. The
adopter owns the SQL string. This page collects the provider-specific
things adopters writing real code need to know.

The four providers we exercise in CI and benchmarks today: **Sqlite** (the
default fixture, in-memory), **PostgreSQL** (Testcontainers in CI), **SQL
Server** (notes only — integration fixture deferred), **MySQL** (notes only
— integration fixture deferred).

## Sqlite

The default fixture provider — in-memory, AOT-friendly, snapshot-stable.

### `decimal` stored as TEXT

Sqlite does not have a native `decimal` type. The
[Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types#decimal)
shim stores `decimal` values as TEXT (using `InvariantCulture` formatting)
and converts back through `decimal.Parse(...)` on read. The shim's
`reader.GetDecimal(ord)` works, but the conversion path allocates the
intermediate string.

For high-throughput Sqlite code, route through `[Materialize(Factory)]`
with `string` factory parameters:

```csharp
[Materialize(Factory = nameof(FromStorage))]
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money FromStorage(string amount, string currency)
        => new(decimal.Parse(amount, CultureInfo.InvariantCulture), currency);
}
```

The factory is committed to **TEXT** storage — see the Postgres note below
for what happens if you point the same factory at a NUMERIC column. The
canonical recipe lives in
[`composites.md`](composites.md#recipe-5--materializefactory-for-sqlite-decimal-as-text).

### `CanCreateBatch = false`

The AdoNet.Async wrapper over Microsoft.Data.Sqlite reports
`CanCreateBatch = false` — Sqlite has no native batch wire protocol. This
affects `BatchMode.Auto` (the default) on multi-result-set returns:

| BatchMode | Sqlite behaviour                                                                          |
| --------- | ----------------------------------------------------------------------------------------- |
| `Auto`    | Branches on `CanCreateBatch`; falls back to single-command `;`-joined SQL with `NextResultAsync`. |
| `Always`  | Throws `NotSupportedException` at runtime — `CreateBatch()` is not implemented.           |
| `Never`   | Same as `Auto` on Sqlite, but explicit.                                                   |

Prefer `BatchMode.Auto` (the default). The fallback path is universally
supported; `Always` is only correct when you know the provider implements
`IAsyncDbBatch`. Details in
[`multi-result-set.md`](multi-result-set.md#choosing-the-dispatch-path-batchmode).

### No real stored procedures

Sqlite has no `CREATE PROCEDURE` syntax. `[StoredProcedure]` doesn't have
a meaningful target on Sqlite — there is no procedure name to call. The
closest equivalents (views, triggers, table-valued user functions) don't
route through `CommandType.StoredProcedure`.

For Sqlite, use `[Query]` (or `[Command]`) with the procedure body
inlined in the SQL string. Stored-procedure integration tests run against
the Postgres fixture; v1.0 ships no Sqlite stored-procedure path.

### `LIMIT` not `TOP`

Sqlite uses `LIMIT N` (Postgres / MySQL too), not SQL Server's
`SELECT TOP N`. Mirrors the rest of the SQL-92 family.

### Row-counted DELETE / UPDATE

Sqlite returns the rows-affected count correctly through
`ExecuteNonQueryAsync` for plain `DELETE` / `UPDATE` statements — no
special handling needed on the `[Command(Kind = NonQuery)]` path.

### `BulkInsert` parameter cap

Sqlite's default per-statement parameter cap is **999**
(`SQLITE_MAX_VARIABLE_NUMBER`). `CommandKind.BulkInsert` chunks at
`900 / placeholderCount` rows per chunk — for a 2-column INSERT that's 450
rows per chunk; for a 10-column INSERT, 90 rows. The 900-parameter budget
deliberately stays under the 999 cap so the emit works on the default
Sqlite build without needing to recompile the engine with a raised
`SQLITE_MAX_VARIABLE_NUMBER`. See
[`bulk-insert.md`](bulk-insert.md#chunking-semantics) for the chunking
behaviour and per-chunk atomicity caveat.

## PostgreSQL (Npgsql)

The primary integration target (Testcontainers fixture in CI).

### Identity returning

Use `RETURNING` on the INSERT — it's the canonical Postgres idiom and
survives transaction isolation cleanly:

```csharp
[Command("INSERT INTO orders (customer_id, total) VALUES (@customerId, @total) RETURNING id",
         Kind = CommandKind.Identity)]
public partial Task<int> InsertAsync(int customerId, decimal total, CancellationToken ct);
```

ZeroAlloc.ORM does **not** auto-append the identity suffix — you write the
`RETURNING` clause yourself. See
[`commands.md`](commands.md#provider-specific-identity-sql) for the full
table of provider-native identity idioms.

### Procedures vs functions

Postgres draws a hard line between **procedures** (introduced in PG 11,
called via `CALL`) and **functions** (called via `SELECT * FROM fn(...)`).
The two route differently through `CommandType`:

| Postgres construct          | ZA.ORM attribute                            |
| --------------------------- | ------------------------------------------- |
| Procedure (`CREATE PROCEDURE`) | `[StoredProcedure("proc_name")]`         |
| Function (`CREATE FUNCTION`)   | `[Query("SELECT * FROM fn(@arg)")]`       |
| Function with OUT params (PG 15+) | `[Query("SELECT * FROM fn(@arg)")]` — Npgsql surfaces OUT as result-set columns |

Routing a function through `[StoredProcedure]` fails at runtime: Npgsql
issues `CALL fn(...)` and Postgres rejects it (functions aren't callable
via `CALL`). The integration suite at
`tests/ZeroAlloc.ORM.Integration.Tests/Postgres/PostgresStoredProcedureTests.cs`
covers the procedure path; the function path is exercised by the regular
`[Query]` integration tests.

### NUMERIC handling

Postgres has a native `NUMERIC` type that maps to CLR `decimal` directly —
`reader.GetDecimal(ord)` is the right read accessor; no factory needed.
The composite recipe (`Money(decimal, string)`) works out of the box
against NUMERIC columns. See
[`composites.md`](composites.md#provider-quirks).

**Caveat: factory parameters must match the column wire type.** A
`[Materialize(Factory)]` factory with a `string amountText` parameter
requires a **TEXT** column on Postgres. Pointing the same factory at a
NUMERIC column throws:

```
InvalidCastException: Reading as 'System.String' is not supported for
fields having DataTypeName 'numeric'
```

This is locked behaviour, codified by
`PostgresMaterializeFactoryTests.Factory_with_string_param_against_NUMERIC_column_throws_InvalidCastException`
in the integration suite. The factory pattern is storage-type-coupled by
design: the column type must match the factory parameter type. Choose
TEXT if you want the factory; choose NUMERIC + plain composite ctor if
you want the native conversion.

### Identifier folding

Unquoted identifiers fold to lowercase server-side
(`SELECT Id FROM Orders` becomes `SELECT id FROM orders` at the server).
For domain-entity reads (name-based binding via
`reader.GetOrdinal("Id")`), this usually still works because Npgsql
returns the unquoted column name lowercased — `GetOrdinal("Id")` matches
case-insensitively in Npgsql.

If your DDL declared `CREATE TABLE Orders ("Id" INTEGER ...)` (quoted to
preserve casing), you must quote in every SELECT too: `SELECT "Id" FROM ...`.
Otherwise the unquoted reference `SELECT Id` looks for a column literally
named `id` and fails with `column "id" does not exist`.

The same folding rule applies to stored-procedure parameter names — keep
the C# parameter casing and the Postgres procedure parameter casing
consistent (lowercase is the safe default).

### Parameter prefix normalisation

ZeroAlloc.ORM emits `@paramName` uniformly across all providers. Npgsql
historically preferred `$1` / `$2` positional placeholders, but every
modern Npgsql release accepts the `@name` form and normalises internally.
No adopter-side change required.

### `BulkInsert` parameter cap

Postgres' per-statement parameter cap is **65535** (an `int16` index on
the wire protocol). `CommandKind.BulkInsert`'s 900-parameter budget leaves
significant headroom: for a 2-column INSERT, ZA.ORM chunks at 450 rows
where Postgres would happily accept ~32k. Chunking therefore rarely fires
for typical schemas, and when it does it's a conservative-by-design
choice (the budget is portable across all four providers, not Postgres-
tuned). A provider-aware chunk size that reads the cap from the
connection is a backlog item. See
[`bulk-insert.md`](bulk-insert.md#per-provider-notes) for the full
per-provider table.

## SQL Server

Notes only — no integration fixture in v1.0. Snapshot tests cover the
emit shape against the SqlClient API surface.

### Identity capture

Two canonical idioms, both supported as plain `[Command(Kind = Identity)]`:

```csharp
// 1) SCOPE_IDENTITY() — session-scoped, the conventional choice.
[Command("INSERT INTO Orders (CustomerId, Total) VALUES (@cust, @total); SELECT SCOPE_IDENTITY()",
         Kind = CommandKind.Identity)]
public partial Task<int> InsertScopeAsync(int cust, decimal total, CancellationToken ct);

// 2) OUTPUT INSERTED.Id — survives triggers; cleaner under transaction isolation.
[Command("INSERT INTO Orders (CustomerId, Total) OUTPUT INSERTED.Id VALUES (@cust, @total)",
         Kind = CommandKind.Identity)]
public partial Task<int> InsertOutputAsync(int cust, decimal total, CancellationToken ct);
```

Prefer `OUTPUT INSERTED.Id` when triggers, replication, or composite-key
scenarios are in play — it returns the actual inserted row's id, not the
last-scoped identity of the session.

### `RETURN value` from sprocs

SQL Server sprocs can return an `int` via the `RETURN value` statement,
alongside any result sets. ZeroAlloc.ORM **discards** the return value by
default. To capture it, add a tuple field named after the conventional
`@RETURN_VALUE` parameter:

```csharp
[StoredProcedure("usp_DoWork")]
public partial Task<(WorkResult Result, int RETURN_VALUE)> DoWorkAsync(
    int input, int RETURN_VALUE, CancellationToken ct);
```

The generator binds `@RETURN_VALUE` as a `Direction = Output` parameter
and reads the procedure's `RETURN` value into the tuple.

### `OUTPUT INSERTED.X` vs output parameters

`OUTPUT INSERTED.X` is a **result-set-producing clause** on `INSERT` /
`UPDATE` / `DELETE` — it returns rows, not output parameters. Pair it with:

- `[Command(Kind = Identity)]` for a single id (single-column OUTPUT).
- `[Query]` for multi-column OUTPUT (returns a row or list).

It is NOT routed through `Direction = Output` parameters — that path is
for procedural `OUTPUT @param` declarations in `CREATE PROCEDURE` bodies.
See [`stored-procedures.md`](stored-procedures.md#provider-quirks).

### Stored-procedure conventions

`[StoredProcedure("usp_X")]` emits the canonical SqlClient EXEC pattern
(`CommandText = "usp_X"`, `CommandType = StoredProcedure`). Parameters
bind by name; output parameters surface through the named-tuple
convention. The named-tuple convention is identical across providers
because the abstraction lives at the ADO.NET surface.

### `BulkInsert` parameter cap

SQL Server's per-statement parameter cap is **2100**.
`CommandKind.BulkInsert`'s 900-parameter budget stays well under this:
for a 2-column INSERT, ZA.ORM chunks at 450 rows where SqlClient would
accept ~1050. Chunking is conservative on SQL Server by design — the
budget is portable across all four providers, not SQL-Server-tuned.
SQL Server is snapshot-only in the v1.3 integration suite; raise a
backlog item if you measure the extra round-trips on a high-cap
provider hurting throughput. See
[`bulk-insert.md`](bulk-insert.md#per-provider-notes) for the full
per-provider table.

## MySQL

Notes only — no integration fixture in v1.0. Snapshot tests cover the
emit shape against the MySql.Data / MySqlConnector API surface.

### Identity capture

```csharp
[Command("INSERT INTO Orders (CustomerId, Total) VALUES (@cust, @total); SELECT LAST_INSERT_ID()",
         Kind = CommandKind.Identity)]
public partial Task<int> InsertAsync(int cust, decimal total, CancellationToken ct);
```

`LAST_INSERT_ID()` is connection-scoped (same semantics as Sqlite's
`last_insert_rowid()` and SQL Server's `SCOPE_IDENTITY()`). The ;-joined
statement form is supported by both MySql.Data and MySqlConnector.

### Stored procedures via `CALL`

MySQL stored procedures use `CALL proc_name(...)` and expose output
parameters through `Parameter.Direction = Output`. The generator's
named-tuple convention works the same way as on SQL Server / Postgres —
see [`stored-procedures.md`](stored-procedures.md#recipe-2--sproc-with-output-parameters).

### Charset / collation

The default `utf8mb4` charset is universally safe. Older `utf8` (3-byte)
columns truncate 4-byte sequences (emoji, supplementary-plane glyphs).
This is a schema-design concern, not a ZA.ORM concern — the generator
emits a single `reader.GetString(ord)` regardless. If you need
character-set sanity, verify in DDL.

### `DECIMAL` handling

MySQL `DECIMAL` columns surface as CLR `decimal` directly through both
official providers; no factory needed. `MEDIUMTEXT`-backed financial
columns (a legacy pattern) need `[Materialize(Factory)]` with a `string`
parameter — same shape as Sqlite's decimal-as-text recipe.

### `BulkInsert` parameter cap

MySQL has no documented per-statement parameter cap separate from the
overall `max_allowed_packet` size — a 16 MiB default in modern releases,
configurable per server. Standard multi-row `VALUES (...), (...), ...` is
supported on both MySql.Data and MySqlConnector.
`CommandKind.BulkInsert` chunks at `900 / placeholderCount` regardless,
which keeps individual statements small enough to fit comfortably inside
the default packet size for any realistic row shape. MySQL is
snapshot-only in the v1.3 integration suite. See
[`bulk-insert.md`](bulk-insert.md#per-provider-notes) for the full
per-provider table.

## Cross-provider tips

### Decimal precision

Always declare explicit precision/scale in DDL — `NUMERIC(18, 4)` is the
common e-commerce shape. Implicit precision varies wildly across
providers (Postgres caps at arbitrary precision, SQL Server defaults to
`DECIMAL(18, 0)`, MySQL to `DECIMAL(10, 0)`). Schema-level explicitness
eliminates the variance.

### Parameter prefixes

ZeroAlloc.ORM emits `@paramName` uniformly. Every provider's modern
driver accepts the `@name` form. Adopters do not need to translate
between `:name` (Oracle / older Npgsql), `$1` (PG positional), or `?`
(MySQL positional) — the generator never emits those.

### NULL semantics

A `DBNull` in a non-nullable column position throws
`ZeroAllocOrmMaterializationException` on every provider. The check is
**generator-emitted**, not provider-dependent — no provider silently
coerces `NULL` to `0` / `""` / `default`. To accept NULLs, mark the
column type nullable in the row record: `decimal? Total`,
`string? PhoneNumber`.

### Connection ownership

ZeroAlloc.ORM follows the
[AdoNet.Async](https://github.com/MarcelRoozekrans/AdoNet.Async) ownership
convention: methods open the connection if needed and close it only if
they opened it. Pre-opened connections (e.g. shared across a transaction)
are left open for the caller to manage. This is provider-independent.

## Related cookbook recipes

- [`flat-row.md`](flat-row.md) — single-row reads with per-provider notes
  on `LIMIT` vs `TOP` and identifier folding.
- [`commands.md`](commands.md) — full table of provider-native identity
  syntaxes (`SCOPE_IDENTITY()` / `RETURNING` / `LAST_INSERT_ID()` /
  `last_insert_rowid()`).
- [`stored-procedures.md`](stored-procedures.md) — procedure-vs-function
  distinction, SQL Server `RETURN value`, MySQL `CALL` semantics.
- [`composites.md`](composites.md) — `[Materialize(Factory)]` for
  decimal-as-text on Sqlite (and on TEXT-backed Postgres columns).
- [`multi-result-set.md`](multi-result-set.md) — `BatchMode.Auto`
  branching on `CanCreateBatch`.
- [`bulk-insert.md`](bulk-insert.md) — `CommandKind.BulkInsert` chunking
  semantics and the 900-parameter portable budget rationale.
- [`streaming.md`](streaming.md) — provider-side cursor lifetimes for
  `IAsyncEnumerable<T>` consumption.

## Related diagnostics

- [`ZAO022`](../diagnostics/ZAO022.md) — return-type shape not supported.
- [`ZAO040`](../diagnostics/ZAO040.md) — no construction strategy resolved
  (typically a column type the generator can't match to a CLR ctor parameter).
- [`ZAO043`](../diagnostics/ZAO043.md) — `[Materialize(Factory)]`
  references a missing / non-static / non-public method.
- [`ZAO051`](../diagnostics/ZAO051.md) — factory parameter name doesn't
  match any SELECT column.
- [`ZAO061`](../diagnostics/ZAO061.md) — `[StoredProcedure("")]` empty
  procedure name (catches placeholders before they reach the driver).
