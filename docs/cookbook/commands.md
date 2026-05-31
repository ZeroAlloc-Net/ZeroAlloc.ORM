# Commands — INSERT / UPDATE / DELETE / scalar aggregates

Add the `[Command]` attribute to a `partial` method to emit a non-query command.
Three "kinds" cover the most common patterns:

| Kind        | When to use                                                              | Returns                          |
| ----------- | ------------------------------------------------------------------------ | -------------------------------- |
| `NonQuery`  | INSERT / UPDATE / DELETE — you want the rows-affected (or none).         | `int` or `void` (via `Task`)     |
| `Scalar`    | `SELECT COUNT(...)`, `SELECT SUM(...)`, single value from a SELECT.      | `T` or `T?`                      |
| `Identity`  | `INSERT ... RETURNING` / `SCOPE_IDENTITY()` / `last_insert_rowid()`.     | `int` / `long` / `Guid` / VO     |

The default kind is `NonQuery`. The other two are opt-in via `Kind =
CommandKind.Scalar` / `CommandKind.Identity`. Pick the kind that matches the
shape the underlying SQL produces — see the recipes below.

> Looking for full result-set materialisation (lists, rows, multi-tuple)?
> That's `[Query]` — see [`multi-result-set.md`](multi-result-set.md) and
> [`streaming.md`](streaming.md). For procedure-driven commands, see
> [`stored-procedures.md`](stored-procedures.md).

## Recipe 1 — NonQuery: count rows affected

The default kind. The generator emits `ExecuteNonQueryAsync` and surfaces the
provider's rows-affected count back to the caller. Use it for INSERT, UPDATE,
DELETE — anything where you care how many rows were touched but don't need a
returned value.

```csharp
using System.Data.Async;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.ORM;

public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO Orders (Id, CustomerId, Total) VALUES (@id, @cust, @total)")]
    public partial Task<int> InsertOrderAsync(int id, int cust, decimal total, CancellationToken ct);

    [Command("UPDATE Orders SET Total = @newTotal WHERE CustomerId = @cust")]
    public partial Task<int> UpdateOrdersByCustomerAsync(int cust, decimal newTotal, CancellationToken ct);

    [Command("DELETE FROM Orders WHERE Id = @id")]
    public partial Task<int> DeleteOrderByIdAsync(int id, CancellationToken ct);
}
```

```csharp
var inserted = await repo.InsertOrderAsync(1, 42, 10.00m, ct).ConfigureAwait(false);
// inserted == 1

var updated = await repo.UpdateOrdersByCustomerAsync(42, 99.99m, ct).ConfigureAwait(false);
// updated == count of rows whose CustomerId matched 42
```

### Discarding the rows-affected count

If you don't care about the count, return `Task` instead of `Task<int>`:

```csharp
[Command("UPDATE Orders SET Total = Total + 1 WHERE Id = @id")]
public partial Task TouchOrderAsync(int id, CancellationToken ct);
```

The generator skips the assignment to the rows-affected local in this shape —
your call site stays clean: `await repo.TouchOrderAsync(1, ct);`.

## Recipe 2 — Scalar: aggregate computations

Set `Kind = CommandKind.Scalar` when the SQL is a SELECT that returns exactly
one value — typically a `COUNT`, `SUM`, `MAX`, `MIN`, or a single-column
single-row SELECT. The generator emits `ExecuteScalarAsync` and unwraps the
result into your declared return type.

```csharp
public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    [Command("SELECT COUNT(*) FROM Orders", Kind = CommandKind.Scalar)]
    public partial Task<int> CountOrdersAsync(CancellationToken ct);

    [Command("SELECT COALESCE(SUM(Total), 0) FROM Orders WHERE CustomerId = @cust",
             Kind = CommandKind.Scalar)]
    public partial Task<decimal> SumTotalsForCustomerAsync(int cust, CancellationToken ct);

    // Nullable scalar — MAX over zero rows returns NULL, which the generator
    // converts to a C# null instead of throwing.
    [Command("SELECT MAX(Created) FROM Orders", Kind = CommandKind.Scalar)]
    public partial Task<DateTime?> MaxCreatedAsync(CancellationToken ct);

    // Value-object return — ConventionKind.SingleArgCtor unwraps the
    // underlying decimal through the VO's primary constructor.
    [Command("SELECT COALESCE(SUM(Total), 0) FROM Orders WHERE CustomerId = @cust",
             Kind = CommandKind.Scalar)]
    public partial Task<TotalAmount> SumTotalsValueObjectAsync(int cust, CancellationToken ct);
}
```

### Null-scalar handling

- **Nullable return type** (`Task<int?>`, `Task<DateTime?>`, `Task<string?>`): a
  NULL from the database — or an empty result set — surfaces as a C# `null`.
- **Non-nullable return type** (`Task<int>`, `Task<decimal>`): a NULL or an
  empty result set throws `InvalidOperationException`. This is a deliberate
  safety net — silently coercing `null` to `0` would hide aggregation bugs in
  production. Wrap the SELECT in `COALESCE(..., 0)` when you want the
  zero-fallback at the database level, or change the return type to
  `Task<int?>` if you genuinely want `null` to flow through.

## Recipe 3 — Identity: capture the inserted row's ID

Set `Kind = CommandKind.Identity` when the SQL is an INSERT that returns the
auto-generated primary key — either via a provider-specific `RETURNING` clause
or a follow-up `SCOPE_IDENTITY()` / `last_insert_rowid()` SELECT. The generator
emits `ExecuteScalarAsync` and unwraps the result into your declared
identity type (`int`, `long`, `Guid`, or a value-object wrapping one of them).

```csharp
public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@cust, @total) RETURNING Id",
             Kind = CommandKind.Identity)]
    public partial Task<int> InsertWithReturningAsync(int cust, decimal total, CancellationToken ct);

    // Same INSERT, returning the strongly-typed OrderId VO instead of a bare int.
    [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@cust, @total) RETURNING Id",
             Kind = CommandKind.Identity)]
    public partial Task<OrderId> InsertWithReturningVOAsync(int cust, decimal total, CancellationToken ct);

    // ;-joined fallback — Sqlite's last_insert_rowid() returns the most recently
    // auto-generated rowid on the current connection. The generator passes the
    // joined SQL through verbatim.
    [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@cust, @total); SELECT last_insert_rowid()",
             Kind = CommandKind.Identity)]
    public partial Task<int> InsertWithLastInsertRowidAsync(int cust, decimal total, CancellationToken ct);
}
```

### Provider-specific identity SQL

The generator does NOT auto-append the provider's identity syntax — you supply
it as part of the SQL string. This keeps the generator provider-agnostic and
lets you pick whichever idiom fits your schema and driver:

| Provider     | Identity syntax                                                                          |
| ------------ | ---------------------------------------------------------------------------------------- |
| PostgreSQL   | `INSERT ... RETURNING "Id"`                                                              |
| Sqlite       | `INSERT ... RETURNING Id` (3.35+) or `INSERT ...; SELECT last_insert_rowid()`            |
| SQL Server   | `INSERT ...; SELECT SCOPE_IDENTITY()` or `INSERT ... OUTPUT INSERTED.Id`                 |
| MySQL        | `INSERT ...; SELECT LAST_INSERT_ID()`                                                    |

The ;-joined `INSERT ...; SELECT ...` form is supported on every provider whose
driver allows multiple statements per `CommandText` — which today includes all
four of the above. Use the provider-native form when available (`RETURNING` /
`OUTPUT`) because it survives transaction isolation cleanly and avoids any
session-state coupling.

### Empty-result safety

If the INSERT returns no row — for example `INSERT ... SELECT ... WHERE 1 = 0
RETURNING Id` — `ExecuteScalarAsync` produces `null`. The generator's Identity
emit throws `InvalidOperationException` with the message **"Identity command
returned no value"** rather than silently defaulting to `0`. The check matches
the non-nullable-scalar guard in Recipe 2: production code should never see a
zero-typed identity that didn't come from the database.

## When NOT to use `[Command]`

- **Full result-set returns** (lists, single rows, tuple-shaped multi-result):
  use `[Query]`. The `[Query]` family covers materialisation; `[Command]` is
  scoped to non-query / scalar / identity.
- **Procedure-driven commands**: use `[StoredProcedure]` — it sets
  `CommandType.StoredProcedure` and handles output parameters via the
  named-tuple convention. See [`stored-procedures.md`](stored-procedures.md).
- **Multi-statement procedural batches** (`BEGIN ... END;` blocks, anonymous
  PL/pgSQL bodies): keep these as plain `[Command(Kind = NonQuery)]` calls —
  the generator passes the SQL through verbatim and the driver runs it.

## Related diagnostics

- [ZAO002](../diagnostics/ZAO002.md) — Return type not supported for the chosen
  `Kind` (e.g. `Task<List<T>>` on a `[Command]`).
- [ZAO005](../diagnostics/ZAO005.md) — More than one ORM attribute on a single
  method.
- [ZAO020](../diagnostics/ZAO020.md) — `[Command(FromResource = true)]` is
  reserved for a later release.

## See also

- [`multi-result-set.md`](multi-result-set.md) — `[Query]` head+lines and other
  tuple-shaped result patterns.
- [`streaming.md`](streaming.md) — `IAsyncEnumerable<T>` for bounded-memory
  iteration over large result sets.
- [`stored-procedures.md`](stored-procedures.md) — `[StoredProcedure]` for
  procedure-driven commands and output parameters.
