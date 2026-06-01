# Single-row reads → flat record

The simplest ZeroAlloc.ORM shape: a `[Query]` returning `Task<T?>` where `T`
is a positional record. The generator emits a single-row materialization
where each ctor parameter reads exactly one column, in declaration order.

| Pattern                          | Use when                                                          |
| -------------------------------- | ----------------------------------------------------------------- |
| `Task<T?>` (nullable)            | Zero or one row — `null` means "no row matched".                  |
| `Task<T>` (non-nullable)         | Exactly one row guaranteed — empty result throws.                 |
| Positional record                | Column → ctor-parameter mapping is purely by **order**.           |
| Single-ctor class (DomainEntity) | Column → ctor-parameter mapping is by **name** (`GetOrdinal`).    |

> Looking for many rows? Use `Task<List<T>>` (eager) or
> `IAsyncEnumerable<T>` (lazy) — see [`streaming.md`](streaming.md). Looking
> for `(head, lines)` tuple returns? See
> [`multi-result-set.md`](multi-result-set.md).

## Recipe 1 — Single-row read by primary key

The canonical shape: a positional record + a parametrised `WHERE` clause +
a nullable `Task<T?>` return.

```csharp
using System.Data.Async;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.ORM;

public sealed record OrderRow(int Id, int CustomerId, decimal Total);

public sealed partial class OrderRepository(IAsyncDbConnection connection)
{
    [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
    public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
}
```

Call site:

```csharp
var order = await repo.GetByIdAsync(42, ct).ConfigureAwait(false);
if (order is null) { /* no order with id = 42 */ }
```

The `?` on `Task<OrderRow?>` signals "no row matched → return null". If you
prefer to throw on empty result, drop the `?`:

```csharp
[Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
public partial Task<OrderRow> GetByIdOrThrowAsync(int id, CancellationToken ct);
```

A non-nullable single-row return throws `ZeroAllocOrmMaterializationException`
when the result set is empty.

## Recipe 2 — Single-row read with value-object fields

Wrap primary-key columns in a value object (single-arg ctor or
`[ValueObject]`) and the generator threads the convention through the
positional-record ctor:

```csharp
using ZeroAlloc.ORM;
using ZeroAlloc.ValueObjects;

[ValueObject]
public readonly partial struct OrderId
{
    public int Value { get; }
    public OrderId(int value) { Value = value; }
    public static OrderId From(int value) => new(value);
}

[ValueObject]
public readonly partial struct CustomerId
{
    public int Value { get; }
    public CustomerId(int value) { Value = value; }
    public static CustomerId From(int value) => new(value);
}

public sealed record OrderRow(OrderId Id, CustomerId CustomerId, decimal Total);

public sealed partial class OrderRepository(IAsyncDbConnection connection)
{
    [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
    public partial Task<OrderRow?> GetByIdAsync(OrderId id, CancellationToken ct);
}
```

The generator wires `@id` to `id.Value` for the bind side, and wraps each
`int` read in `OrderId.From(...)` / `CustomerId.From(...)` on the read side.
No reflection — the conventions resolve at compile time.

## Recipe 3 — Single-row read with nullable columns

Per-column nullability is expressed on the **record ctor parameter** type.
`decimal?` accepts `DBNull`; `decimal` does not.

```csharp
public sealed record CustomerRow(
    int Id,
    string Name,
    string? PhoneNumber,    // nullable VARCHAR — DBNull → null
    decimal? LifetimeValue, // nullable NUMERIC — DBNull → null
    DateTime CreatedAt);    // NOT NULL — DBNull throws

public sealed partial class CustomerRepository(IAsyncDbConnection connection)
{
    [Query("""
        SELECT Id, Name, PhoneNumber, LifetimeValue, CreatedAt
        FROM Customers WHERE Id = @id
        """)]
    public partial Task<CustomerRow?> GetByIdAsync(int id, CancellationToken ct);
}
```

A `DBNull` in a non-nullable column position throws
`ZeroAllocOrmMaterializationException` with a message naming the offending
column. The throw is intentional — a silent zero/empty-string default would
hide schema-drift bugs in production.

> Reference-type nullability requires `<Nullable>enable</Nullable>` on the
> consuming project. Without nullable context, the `?` annotation is
> erased and the generator falls back to non-nullable semantics.

## Recipe 4 — Column name vs position resolution

ZeroAlloc.ORM picks **one** of two resolution strategies based on the
target type's construction shape:

| Type shape                          | Resolution         | Sensitive to                |
| ----------------------------------- | ------------------ | --------------------------- |
| Positional record (`record T(...)`) | **By position**    | SELECT column **order**     |
| Single-ctor class (DomainEntity)    | **By column name** | SELECT column **names**     |

### Positional record — order matters

```csharp
public sealed record OrderRow(int Id, int CustomerId, decimal Total);

// WORKS — columns line up with ctor parameters by position.
[Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);

// BROKEN — Total reads CustomerId's value, CustomerId reads Total's value.
[Query("SELECT Id, Total, CustomerId FROM Orders WHERE Id = @id")]
public partial Task<OrderRow?> GetByIdBrokenAsync(int id, CancellationToken ct);
```

Column **names** in the SELECT are ignored at this position; reorder the
SELECT to reorder the reads. The generator emits one `reader.GetXxx(ord)`
per ctor parameter, with `ord` ascending from 0.

### Domain entity — names matter

```csharp
public sealed class OrderEntity
{
    public int Id { get; }
    public int CustomerId { get; }
    public decimal Total { get; }

    public OrderEntity(int Id, int CustomerId, decimal Total)
    {
        this.Id = Id;
        this.CustomerId = CustomerId;
        this.Total = Total;
    }
}

// WORKS — every ctor parameter resolves via reader.GetOrdinal("...").
//         SELECT column order is irrelevant.
[Query("SELECT Total, Id, CustomerId FROM Orders WHERE Id = @id")]
public partial Task<OrderEntity?> GetByIdAsync(int id, CancellationToken ct);
```

For a domain entity, the generator emits
`var __o_Id = __reader.GetOrdinal("Id");` once per column, then threads
the ordinal through both the `IsDBNull` and `GetXxx` calls. The SELECT
can project columns in any order.

> The "name vs position" split was a deliberate v0.2 design choice: records
> are about shape, classes are about identity. Records compose with the
> positional convention (the ctor is generated from the parameter list);
> classes don't, so they fall back to name-based binding.

## Provider quirks

Three short notes that bite single-row reads in particular:

- **Sqlite `decimal` columns** — Sqlite stores `decimal` as TEXT. The
  Microsoft.Data.Sqlite shim converts on `reader.GetDecimal(ord)`, but the
  conversion allocates. For hot single-row reads, route through
  `[Materialize(Factory)]` — see
  [`composites.md`](composites.md#recipe-5--materializefactory-for-sqlite-decimal-as-text).
- **Postgres identifier folding** — unquoted identifiers fold to lowercase
  (`SELECT Id` becomes `SELECT id` at the server). For domain-entity reads
  (name-based binding), this means `__reader.GetOrdinal("Id")` returns the
  column whose **server-side** name is `id` — usually fine, but quoting in
  the SELECT (`"Id"`) preserves casing if your DDL declared it that way.
- **SQL Server `TOP 1`** — single-row reads with a `LIMIT 1` clause work on
  Sqlite, Postgres, and MySQL but not SQL Server. Use `TOP 1` on SQL Server
  instead: `SELECT TOP 1 Id, CustomerId, Total FROM Orders WHERE ...`.

See [`provider-quirks.md`](provider-quirks.md) for the consolidated
per-provider catalog.

## When NOT to use this pattern

- **Many rows** — use `Task<List<T>>` (eager) or `IAsyncEnumerable<T>`
  (lazy). See [`streaming.md`](streaming.md).
- **Scalar aggregates** — `SELECT COUNT(*) FROM ...` belongs on
  `[Command(Kind = CommandKind.Scalar)]`. See
  [`commands.md`](commands.md#recipe-2--scalar-aggregate-computations).
- **`(head, lines)` tuple shape** — when the row arrives alongside a list
  of related rows. See [`multi-result-set.md`](multi-result-set.md).
- **Multi-column composites** — a multi-column value-object field
  (`Money(decimal, string)`) is supported through the composite pattern.
  See [`composites.md`](composites.md).

## Related diagnostics

- [`ZAO001`](../diagnostics/ZAO001.md) — method must be `partial`.
- [`ZAO004`](../diagnostics/ZAO004.md) — containing type must be `partial`.
- [`ZAO022`](../diagnostics/ZAO022.md) — return-type shape not supported.
- [`ZAO040`](../diagnostics/ZAO040.md) — no construction strategy resolved
  for the record / domain-entity type.
- [`ZAO041`](../diagnostics/ZAO041.md) — no binding strategy resolved for
  a method parameter.
- [`ZAO044`](../diagnostics/ZAO044.md) — ambiguous convention discovery.

## See also

- [`multi-result-set.md`](multi-result-set.md) — `(head, lines)` and other
  tuple-shaped multi-result patterns.
- [`streaming.md`](streaming.md) — `IAsyncEnumerable<T>` for unbounded
  result sets.
- [`commands.md`](commands.md) — `[Command]` for non-query / scalar /
  identity returns.
- [`composites.md`](composites.md) — multi-column composite fields
  (`Money(decimal, string)`) nested inside a flat row.
- [`stored-procedures.md`](stored-procedures.md) — `[StoredProcedure]` over
  a procedure that yields a single row.
- [`provider-quirks.md`](provider-quirks.md) — provider-specific behaviour
  (decimal-as-text, identifier folding, `LIMIT` vs `TOP`).
