# Stored procedures — `[StoredProcedure]`

Add `[StoredProcedure("procedure_name")]` to a `partial` method to call a real
stored procedure. The generator emits `CommandType = StoredProcedure` and the
procedure name on the command. Parameters are bound by name from the method
signature; return shapes mirror `[Query]` — single row, list, multi-result-set
tuple, or output-parameter tuple.

Behaviorally, `[StoredProcedure("usp_X")]` is equivalent to a hypothetical
`[Query(name, CommandType = StoredProcedure)]`, but the dedicated attribute
keeps the surface discoverable in domain code and unlocks the named-tuple
output-parameter convention covered in Recipe 2.

> Looking for plain-SQL commands? See [`commands.md`](commands.md) for
> `[Command]`. Looking for Postgres functions or SQL Server table-valued
> functions? Those go through `[Query]` because they're called via
> `SELECT * FROM fn(@x)`, not `CommandType.StoredProcedure`. See **Provider
> quirks** at the bottom of this page.

## Recipe 1 — Simple sproc returning one row

```csharp
using System.Data.Async;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.ORM;

public sealed record OrderRow(int Id, int CustomerId, decimal Total);

public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    [StoredProcedure("usp_GetOrder")]
    public partial Task<OrderRow?> GetOrderAsync(int id, CancellationToken ct);
}
```

Call site:

```csharp
var order = await repo.GetOrderAsync(42, ct).ConfigureAwait(false);
if (order is null) { /* not found */ }
```

The generator:

1. Creates a command with `CommandText = "usp_GetOrder"` and
   `CommandType = StoredProcedure`.
2. Adds one parameter named `id` carrying the `int` argument.
3. Materialises the first row of the result set into an `OrderRow`. If the
   reader yields zero rows, the nullable return shape surfaces as `null`.

The same single-row, list, and multi-result-set conventions used by `[Query]`
apply here — see [`multi-result-set.md`](multi-result-set.md) for the full
shape matrix.

## Recipe 2 — Sproc with output parameters

Procedures with `OUTPUT` parameters surface them through a **named-tuple return
shape**: tuple field names match C# parameter names (case-insensitive). The
generator notices the match and flips the matching parameter's `Direction` to
`Output` instead of `Input`, then assigns the parameter's `.Value` to the
corresponding tuple position once the command finishes.

```csharp
public sealed record OrderRow(int Id, int CustomerId, decimal Total);

public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    [StoredProcedure("usp_InsertOrder")]
    public partial Task<(OrderRow Result, int NewOrderId)> InsertAsync(
        int customerId,
        int newOrderId,
        CancellationToken ct);
}
```

In this signature:

- `Result` doesn't match any C# parameter — it's the **result-row position**,
  materialised from the procedure's result set.
- `NewOrderId` matches the `newOrderId` parameter (case-insensitive) — that
  parameter is bound as `Direction = Output`, and `.Value` is unwrapped into
  the tuple's `NewOrderId` slot after the command completes.

Call site:

```csharp
var (order, newId) = await repo.InsertAsync(customerId: 42, newOrderId: 0, ct)
    .ConfigureAwait(false);
// `newId` carries the value the procedure assigned to `@NewOrderId OUTPUT`.
```

The argument you pass for an output-bound parameter is used to seed
`Parameter.Value` before the call; most procedures ignore the seed, but the
binding is preserved for the rare procedure that uses it as a hint.

### Reader-drain semantics

The generator emits a reader-drain loop **before** reading output-parameter
values. Most providers — SqlClient, Npgsql, Microsoft.Data.Sqlite — only
populate `Parameter.Value` on output-bound parameters once the data reader
has been fully consumed (and in some cases, closed). Reading `.Value` mid-stream
yields stale or null data.

Practically, this means:

- **Don't break out of `await foreach` early** on a sproc whose return shape
  carries output parameters. The generator's drain loop sits between the row
  consumption and the output-value reads; an early break still triggers the
  iterator's `DisposeAsync`, but you lose the output values if the drain
  doesn't complete.
- **Single-row and tuple return shapes are safe** — the generator's emit
  already consumes the reader fully before touching `.Value`.

## Recipe 3 — Output-only sproc

When the procedure produces no result set — only output parameters — the
generator detects the shape (every tuple field maps to a parameter) and emits
`ExecuteNonQueryAsync` instead of opening a reader.

```csharp
public sealed partial class IdAllocator(IAsyncDbConnection connection)
{
    [StoredProcedure("usp_AllocateId")]
    public partial Task<(int NewOrderId, int Status)> AllocateAsync(
        int newOrderId,
        int status,
        CancellationToken ct);
}
```

Both `NewOrderId` and `Status` match parameters; the procedure has no result
rows to materialise; the emit calls `ExecuteNonQueryAsync` and reads the two
output values directly off the command's parameter collection.

Call site:

```csharp
var (newId, status) = await allocator.AllocateAsync(0, 0, ct).ConfigureAwait(false);
```

## Recipe 4 — Multi-result-set sproc

The head+lines pattern works for sprocs too. The same `(Head, IReadOnlyList<Line>)?`
tuple shape used by `[Query]` (see [`multi-result-set.md`](multi-result-set.md))
applies here — the difference is just that the procedure name lives in the
attribute instead of the SQL.

```csharp
public sealed record OrderRow(int Id, int CustomerId, decimal Total);
public sealed record OrderLineRow(int Id, int OrderId, string Sku, int Qty);

public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    [StoredProcedure("usp_GetOrderWithLines")]
    public partial Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?> GetWithLinesAsync(
        int id,
        CancellationToken ct);
}
```

The procedure body should issue two SELECTs in sequence — the first projects
the head row, the second the lines. The generator walks the reader with
`NextResultAsync` between the two materialisation passes, exactly as it would
for a `;`-joined `[Query]`.

> The outer-nullable shape (`Task<(... )?>`) carries the "head row may be
> missing" semantic: if the procedure returns zero rows in the first result
> set, the whole tuple surfaces as `null`. Drop the `?` if your procedure
> guarantees a head row.

## Recipe 5 — Mixed result-set + output parameters

A procedure that returns both a result set AND output parameters combines the
patterns in Recipes 2 and 4. Tuple fields that match parameter names become
output bindings; non-matching fields become result-set positions.

```csharp
public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    [StoredProcedure("usp_InsertOrderReturning")]
    public partial Task<(OrderRow Result, int NewOrderId)> InsertReturningAsync(
        int customerId,
        int newOrderId,
        CancellationToken ct);
}
```

The procedure inserts a row, returns it as a single-row result set, and exposes
the new id as an `OUTPUT` parameter. The generator's reader-drain ordering
guarantees the row materialises first, then the output value is read.

Two common pitfalls in this shape are caught at build time:

- **More than one non-matching tuple field** beyond the conventional "result"
  position trips [ZAO062](../diagnostics/ZAO062.md) — usually a typo'd output
  field name.
- **Empty `[StoredProcedure("")]`** trips [ZAO061](../diagnostics/ZAO061.md) —
  catches accidental placeholder values before they reach the driver.

## Provider quirks

Stored-procedure semantics vary more than plain SQL does. The generator emits
provider-agnostic ADO.NET (`CommandType.StoredProcedure` + parameters); the
provider is responsible for translating that into the right wire syntax. The
notable differences:

- **PostgreSQL — procedures vs functions.** Procedures (introduced in PG 11)
  are called via `CALL proc_name(...)` and DO use
  `CommandType.StoredProcedure` through Npgsql. Functions — including
  rowset-returning functions — are NOT called this way; they're invoked as
  `SELECT * FROM fn(@arg)` and belong on `[Query]` instead. Routing a
  function through `[StoredProcedure]` will fail at runtime.
- **PostgreSQL output parameters.** Procedures with OUT parameters work
  through Npgsql via `Parameter.Direction = Output` — same generator emit as
  SQL Server. The caller must pass placeholder values for OUT positions; the
  CALL command's wire syntax includes them as `NULL` markers.
- **SQL Server `RETURN value`.** SQL Server sprocs can return an `int` via
  `RETURN value` alongside any result sets. The generator treats the return
  value as **discarded by default**. If you need to capture it, add a tuple
  field matching the conventional `@RETURN_VALUE` parameter name and the
  generator binds it as an `Output` parameter against that slot.
- **SQL Server `OUTPUT INSERTED.X`.** This is a result-set-producing clause,
  not an output parameter — pair it with `[Command(Kind = Identity)]` for a
  single id or `[Query]` for multi-column inserts. See
  [`commands.md`](commands.md#provider-specific-identity-sql).
- **Sqlite has no native stored procedures.** There's no `CREATE PROCEDURE`
  syntax, and the closest equivalents (views, triggers, table-valued user
  functions) don't route through `CommandType.StoredProcedure`. Integration
  tests for `[StoredProcedure]` ship against a Postgres / SQL Server fixture
  in a future release; v0.4 verifies the emit shape via snapshot tests only.
- **MySQL sprocs.** MySQL stored procedures use `CALL proc_name(...)` and
  expose output parameters through MySql.Data / MySqlConnector's
  `Parameter.Direction = Output`. The generator's named-tuple convention
  works the same way.

## When NOT to use `[StoredProcedure]`

- **Postgres functions returning rowsets.** Use
  `[Query("SELECT * FROM fn(@id)")]` instead. The procedure-vs-function
  distinction matters on PG; using `CommandType.StoredProcedure` against a
  function fails at runtime.
- **Plain SQL with no procedure involved.** Use `[Query]` for SELECTs and
  `[Command]` for INSERT / UPDATE / DELETE / scalar / identity. The dedicated
  attributes are clearer at the call site and don't carry the
  `CommandType.StoredProcedure` flip.
- **Inline anonymous code blocks** (PL/pgSQL `DO $$ ... $$;`, T-SQL `BEGIN ...
  END;`). These run via plain `[Command]` — the driver executes the body
  directly without the procedure-call wire path.

## Related diagnostics

- [ZAO005](../diagnostics/ZAO005.md) — More than one ORM attribute on a single
  method (e.g. both `[Query]` and `[StoredProcedure]`).
- [ZAO060](../diagnostics/ZAO060.md) — `[StoredProcedure]` async method has
  `out`/`ref` parameter (reserved; CS1988 covers today).
- [ZAO061](../diagnostics/ZAO061.md) — `[StoredProcedure("")]` with an empty
  or whitespace-only procedure name.
- [ZAO062](../diagnostics/ZAO062.md) — Named-tuple field doesn't match any
  C# parameter — likely typo'd output binding.

## See also

- [`commands.md`](commands.md) — `[Command]` for plain-SQL INSERT / UPDATE /
  DELETE / scalar / identity.
- [`multi-result-set.md`](multi-result-set.md) — tuple-shaped multi-result
  patterns (head + lines, count + first + all).
- [`streaming.md`](streaming.md) — `IAsyncEnumerable<T>` over large result
  sets. (Sprocs work here too — wrap a procedure-driven stream with the same
  attribute shape.)
