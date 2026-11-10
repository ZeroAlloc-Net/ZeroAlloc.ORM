# Single-row reads → flat record

The simplest ZeroAlloc.ORM shape: a `[Query]` returning `Task<T?>` where `T`
is a positional record. The generator emits a single-row materialization
where each ctor parameter reads exactly one column, in declaration order.

| Pattern                          | Use when                                                          |
| -------------------------------- | ----------------------------------------------------------------- |
| `Task<T?>` (nullable)            | Zero or one row — `null` means "no row matched".                  |
| Positional record                | Column → ctor-parameter mapping is purely by **order**.           |
| Single-ctor class (DomainEntity) | Column → ctor-parameter mapping is by **name** (`GetOrdinal`).    |

Every `Task<T>` shape in this recipe, and in the list, multi-result-set and
stored-procedure recipes, can be declared as `ValueTask<T>` instead. The emitted
body is the same `async` body. When the provider completes the call synchronously,
as in-memory Sqlite does, a `ValueTask<T>` query does not allocate the `Task`:
measured on Sqlite, `SELECT 42` as `Task<int>` allocates 944 B per call and as
`ValueTask<int>` 872 B, the difference being the 72 B `Task<int>`. The rest is the
provider's. A caller may await a `ValueTask<T>` only once.

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

The `?` on `Task<OrderRow?>` signals "no row matched → return null". A single
row is always `Task<TRow?>`: if you need an exception when no row matches, throw
at the call site:

```csharp
var order = await repo.GetByIdAsync(42, ct).ConfigureAwait(false)
    ?? throw new InvalidOperationException("Order 42 not found.");
```

A non-nullable `Task<OrderRow>` for a record or class row is a build error,
[ZAO022](../diagnostics/ZAO022.md), whose message points at `Task<OrderRow?>`.
A value-type composite at scalar position, such as `Task<Money>` for a
`readonly record struct Money(decimal Amount, string Currency)`, may still be
declared non-nullable; see [composites.md](composites.md).

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
column, the constructor parameter it binds to and that parameter's type:

```text
Column 'CreatedAt' is NULL, but parameter 'CreatedAt' of 'MyApp.CustomerRow' is the non-nullable type 'System.DateTime'. Declare it as 'System.DateTime?' to receive null.
```

The throw is intentional — a silent zero/empty-string default would
hide schema-drift bugs in production. The same check covers enums, value
objects, the inner columns of a composite such as `Money`, and every row
shape: a single row, a list, a stream and a multi-result tuple. A composite
declared nullable (`Money?`) keeps its all-or-nothing rule instead; see
[composites](composites.md). The provider's own exception is kept as
`InnerException`, and the check costs nothing on a row without a NULL; see
[provider quirks](provider-quirks.md#null-semantics).

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
`var __Id_ord = __reader.GetOrdinal("Id");` once per column, then threads
the ordinal through both the `IsDBNull` and `GetXxx` calls. The SELECT
can project columns in any order. A method that reads many rows, a
`Task<IReadOnlyList<T>>`, an `IAsyncEnumerable<T>` or a list in a
multi-result tuple, resolves the ordinals once, before its first row,
not once per row.

> The "name vs position" split was a deliberate v0.2 design choice: records
> are about shape, classes are about identity. Records compose with the
> positional convention (the ctor is generated from the parameter list);
> classes don't, so they fall back to name-based binding.

## Table and column placeholders

A library that ships repositories cannot know the table its adopters use, so the SQL of a `[Query]` or
`[Command]` can name it with a `{table}` placeholder. The generator binds it to a `string` member of the
repository: a primary-constructor parameter, a field or a property with a getter.

```csharp
[Dialect(SqlDialect.Sqlite)]
public sealed partial class OrderRepository(IAsyncDbConnection connection, string table, bool quoteColumns)
{
    [Query("SELECT COUNT(*) FROM {table} WHERE Status = @status")]
    public partial Task<int> CountAsync(string status, CancellationToken ct);

    [Query("SELECT {col:Id}, {col:Name} FROM {table} WHERE {col:Id} = @id")]
    public partial Task<OrderRow?> GetAsync(int id, CancellationToken ct);
}
```

`{table}` binds to `table`, ignoring case and one leading underscore, so `_table` works too. `{col:Id}`
writes a column name. It is quoted in the style of the declared `[Dialect]` when the repository has a `bool`
member named `QuoteColumns` that is true, and bare otherwise: `"Id"` for SQLite and PostgreSQL, `[Id]` for
SQL Server, a backtick on both sides for MySQL. Without a declared dialect `{col:Id}` is a build error.
Here the member `quoteColumns` carries the flag.

The rules:

- Placeholders work in `[Command]` and `[Query]` SQL, outside quotes and comments. `'{1,2}'` and
  `-- {x}` are not placeholders. `BulkInsert` and `[StoredProcedure]` names do not take them (ZAO117).
- Members are read on every call, so a changed value is honoured on the next call. A repository instance holds
  no state for placeholders.
- The SQL for each distinct value is built once and cached process-wide, so a call allocates the same as a
  call with literal SQL.
- A null or blank table throws `ArgumentException` on each call.
- A placeholder belongs where a name belongs. Use `@parameter` for values; the build refuses a placeholder
  after `=`, `LIKE` or `IN (` or in a `VALUES` tuple (ZAO116).

The table value is configuration, not input: it is spliced into the SQL verbatim and is never escaped or validated, so never pass user input.

The SQL for each distinct value is cached for the life of the process and never evicted, so keep the
set of values small and fixed, such as one per deployment or pipeline, not one per request or per tenant.

| ID | Severity | Fires when |
| --- | --- | --- |
| [ZAO115](../diagnostics/ZAO115.md) | Error | A placeholder finds no `string` member, finds several, or `QuoteColumns` is not a `bool`. |
| [ZAO116](../diagnostics/ZAO116.md) | Error | A placeholder sits where a value belongs. |
| [ZAO117](../diagnostics/ZAO117.md) | Error | A placeholder is in `BulkInsert` or stored-procedure SQL. |
| [ZAO118](../diagnostics/ZAO118.md) | Error | `{col:Name}` is used on a repository with no declared dialect. |

**Upgrading.** This is a behaviour change: a `{identifier}` or `{col:identifier}` in existing `[Query]` or
`[Command]` SQL outside quotes and comments is now a placeholder. A `{identifier}` binds silently when the
repository has a `string` member of the same name, and fails the build with ZAO115 otherwise. A
`{col:identifier}` becomes a column name, and fails the build with ZAO118 when no dialect is declared. The
scanner does not honour backslash escapes: in MySQL, or a PostgreSQL `E'...'` string, a `\'` inside a literal
ends the literal for the scanner, so brace text after it can become a placeholder. Move such brace text
inside a string literal that escapes quotes by doubling them, or remove it.

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
