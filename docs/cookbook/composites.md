# Multi-column composites — `Money(decimal Amount, string Currency)` pattern

Declare a multi-arg-ctor type like `Money(decimal Amount, string Currency)` and
the generator unpacks it into N SQL columns automatically. Use
`[Materialize(Factory = "...")]` for advanced cases (Sqlite `decimal`-as-text,
custom validation, provider-specific decoding).

| Pattern                          | Use when                                                                  |
| -------------------------------- | ------------------------------------------------------------------------- |
| Composite                        | A value spans 2+ columns that always travel together (Money, Address).    |
| Composite nested in a row        | A row contains a composite field (`OrderRow.Total`).                      |
| Composite method parameter       | A command/query takes a composite as input (UPDATE ... SET amount, ccy).  |
| Nullable composite               | The composite columns can be NULL together (`Money?`).                    |
| `[Materialize(Factory)]`         | The SQL shape doesn't match the C# ctor (Sqlite TEXT, custom validation). |

> Looking for single-column value objects (`OrderId(int Value)`)? Use the
> `[ValueObject]` attribute or the single-arg-ctor convention — see the
> v0.2 release notes. Composites are for the multi-column case.

## Recipe 1 — Simple multi-column composite

Declare the composite as a positional record (struct or class) with N
constructor parameters. Each parameter must resolve to a single column — a
primitive, an enum, a value object, or a `static From(...)` factory:

```csharp
using System.Data.Async;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.ORM;

public readonly record struct Money(decimal Amount, string Currency);

public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
    public partial Task<Money> GetTotalAsync(int id, CancellationToken ct);
}
```

The generator emits one `reader.GetXxx(ord)` per ctor parameter and constructs
the composite directly:

```csharp
return new Money(__reader.GetDecimal(0), __reader.GetString(1));
```

The SELECT column ORDER drives the read order — `SELECT Amount, Currency`
binds `GetDecimal(0)` to `Amount` and `GetString(1)` to `Currency`. SELECT
column names are NOT used at this position; reorder the SELECT to reorder the
reads.

## Recipe 2 — Composite nested in a row

A composite field nests inside a `FlatRow` (positional record) or
`DomainEntity` (single-ctor class). The outer ctor's column count expands to
include the composite's inner columns:

```csharp
public readonly record struct Money(decimal Amount, string Currency);
public sealed record OrderRow(int Id, Money Total);

public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Id, Amount, Currency FROM Orders WHERE Id = @id")]
    public partial Task<OrderRow?> GetOrderAsync(int id, CancellationToken ct);
}
```

The SELECT lists THREE columns even though `OrderRow` has TWO ctor parameters.
The emit threads the column index through the composite's inner reads:

```csharp
return new OrderRow(
    __reader.GetInt32(0),                                         // Id
    new Money(__reader.GetDecimal(1), __reader.GetString(2)));    // Total
```

Composite fields can be deeply layered with single-arg conventions —
`Money(decimal, OrderId)` where `OrderId` is itself a value object reads
`new Money(__reader.GetDecimal(N), OrderId.From(__reader.GetInt32(N+1)))`.
Composite-of-composite (a ctor parameter that is itself a multi-column
composite) is **not** supported in v0.5 — see Recipe 5 for the workaround
and [`ZAO052`](../diagnostics/ZAO052.md) for the diagnostic.

## Recipe 3 — Composite parameter binding

A composite method parameter unpacks into N `DbParameter`s, one per ctor arg.
The wire-level parameter name is `@{paramName}_{ctorArgName}` (positional
suffix derived from the C# names):

```csharp
public readonly record struct Money(decimal Amount, string Currency);

public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    [Command("UPDATE Orders SET Amount = @total_Amount, Currency = @total_Currency WHERE Id = @id",
        Kind = CommandKind.NonQuery)]
    public partial Task<int> UpdateTotalAsync(int id, Money total, CancellationToken ct);
}
```

The SQL author picks the parameter-name suffix scheme by spelling out
`@total_Amount` / `@total_Currency` in the SQL string — the generator emits
matching `DbParameter.ParameterName` values:

```csharp
var __p_total_Amount    = __cmd.CreateParameter();
__p_total_Amount.ParameterName = "@total_Amount";
__p_total_Amount.Value = @total.@Amount;
__cmd.Parameters.Add(__p_total_Amount);

var __p_total_Currency  = __cmd.CreateParameter();
__p_total_Currency.ParameterName = "@total_Currency";
__p_total_Currency.Value = @total.@Currency;
__cmd.Parameters.Add(__p_total_Currency);
```

`[Param(Name = "...")]` is NOT supported on composite parameters — see
[`ZAO063`](../diagnostics/ZAO063.md). To rename, rename the C# parameter
(`Money totalMoney` -> `@totalMoney_Amount`).

## Recipe 4 — Nullable composite (all-or-nothing)

Declaring the composite nullable (`Money?` on either side) opts into the
**all-or-nothing** runtime contract:

```csharp
[Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
public partial Task<Money?> GetTotalAsync(int id, CancellationToken ct);
```

Materialization semantics:

- All composite columns are `DBNull` -> the method returns `null`.
- All columns are non-null -> the method returns `new Money(...)`.
- ONE column is `DBNull` but not the others -> the method throws
  `ZeroAllocOrmMaterializationException` with a message listing which inner
  columns were null.

`[ZAO050](../diagnostics/ZAO050.md)` fires at compile time for every nullable
composite position — the generator can't statically prove that the schema
declares the columns NOT NULL together, so it surfaces the runtime check
explicitly. Suppress when the schema enforces all-or-nothing:

```xml
<NoWarn>$(NoWarn);ZAO050</NoWarn>
```

or per-method:

```csharp
#pragma warning disable ZAO050
[Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
public partial Task<Money?> GetTotalAsync(int id, CancellationToken ct);
#pragma warning restore ZAO050
```

The same semantics apply to nullable composite parameters — `Money? total`
binds `DBNull` to each inner parameter when `total is null`.

## Recipe 5 — `[Materialize(Factory)]` for Sqlite decimal-as-text

Sqlite stores `decimal` values as TEXT — `reader.GetDecimal(ord)` works against
the Microsoft.Data.Sqlite shim but allocates, and provider-native code
(`GetString` + `decimal.Parse`) avoids the runtime conversion. The
`[Materialize(Factory)]` attribute lets you point the generator at a custom
static factory that drives the SELECT shape:

```csharp
using System.Globalization;
using ZeroAlloc.ORM;

[Materialize(Factory = "FromStorage")]
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money FromStorage(string amount, string currency)
        => new Money(decimal.Parse(amount, CultureInfo.InvariantCulture), currency);
}

public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
    public partial Task<Money> GetTotalAsync(int id, CancellationToken ct);
}
```

The factory's PARAMETER TYPES define the reader methods — `string amount` ->
`reader.GetString(0)`, `string currency` -> `reader.GetString(1)`:

```csharp
return Money.FromStorage(__reader.GetString(0), __reader.GetString(1));
```

The factory's PARAMETER NAMES drive name-based matching against SELECT column
names (case-insensitive). If your SELECT column name doesn't match a factory
parameter name, either rename the factory parameter, add a SQL `AS` alias, or
align the SELECT column order — see [`ZAO051`](../diagnostics/ZAO051.md) for
the diagnostic and full fallback rules.

## Recipe 6 — Method-level factory override

The `[Materialize(Factory)]` annotation can sit on the return type position of
a specific method, overriding any type-level factory:

```csharp
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money FromTotalCents(long totalCents, string currency)
        => new Money(totalCents / 100m, currency);
}

public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    // Uses Money.FromTotalCents for THIS method only — other methods on the
    // same repo continue to use the default convention (or type-level factory).
    [return: Materialize(Factory = "FromTotalCents")]
    [Query("SELECT TotalCents, Currency FROM Orders")]
    public partial Task<Money> GetTotalCentsAsync(CancellationToken ct);
}
```

`[return: Materialize]` wins over any type-level `[Materialize]` attribute on
the return element type. Use this when one query in a repository deviates from
the canonical materialization shape (a legacy column, a JOIN-flattened view, a
materialised-view-only column).

## Combinations: composite x value-object

Composites compose with `[ValueObject]` / single-arg-ctor convention at every
position. Common shapes:

```csharp
[ValueObject]
public readonly partial struct OrderId
{
    public int Value { get; }
    public OrderId(int v) { Value = v; }
    public static OrderId From(int value) => new(value);
}

public readonly record struct Money(decimal Amount, string Currency);

// VO inside a composite — `Money(decimal, OrderId Currency)`
public readonly record struct MoneyWithOrderId(decimal Amount, OrderId Currency);

// VO at OUTER position + composite nested.
public sealed record OrderRow(OrderId Id, Money Total);

// VO + composite as separate parameters.
[Command("UPDATE Orders SET Amount = @total_Amount, Currency = @total_Currency WHERE Id = @orderId",
    Kind = CommandKind.NonQuery)]
public partial Task<int> UpdateAsync(Money total, OrderId orderId, CancellationToken ct);
```

The generator threads each convention through independently — the VO unwrap
(`@orderId.Value`) and the composite unpack (`@total_Amount` / `@total_Currency`)
don't interact.

## Provider quirks

- **Sqlite stores `decimal` as TEXT** — `reader.GetDecimal(ord)` works (via
  Microsoft.Data.Sqlite's runtime conversion) but allocates. For high-throughput
  Sqlite code, use `[Materialize(Factory)]` with `string` factory parameters and
  `decimal.Parse(InvariantCulture)` inside the factory.
- **PostgreSQL / SQL Server** — both surface `numeric`/`decimal` columns as
  CLR `decimal` directly; no factory needed.
- **MySQL** — `DECIMAL` columns surface as `decimal` via the official
  provider; `MEDIUMTEXT`-backed financial columns need `[Materialize(Factory)]`.

See [`provider-quirks.md`](provider-quirks.md) for the full per-provider
catalog (decimal-as-text, identifier folding, batch support, identity
syntax, and more).

## When NOT to use composites

- **1-arg wrappers** — use the value-object pattern (`[ValueObject]` or a
  single-arg ctor). Composites are for the multi-column case; a 1-arg ctor type
  is classified as `SingleArgCtor` / `ValueObject`, not a composite.
- **1-arg unique-validation wrappers** — use `[Materialize(Factory)]` with a
  custom validation factory (`PositiveDecimal.FromValidated(decimal)`).
- **Composites-of-composites** — v0.5 emits
  [`ZAO052`](../diagnostics/ZAO052.md). Either flatten the inner composite
  into the outer ctor, or use `[Materialize(Factory)]` to keep the C# shape
  intact while the factory drives the SELECT shape.
- **Sparse columns** — if your row has many fields but most are NULL most of
  the time, prefer `FlatRow` with per-column nullable annotations over
  packing them into composites.

## Related diagnostics

- [`ZAO022`](../diagnostics/ZAO022.md) — Return type shape not yet supported.
- [`ZAO040`](../diagnostics/ZAO040.md) — No construction strategy resolved for
  the composite type (no factory, no positional ctor, no convention match).
- [`ZAO041`](../diagnostics/ZAO041.md) — No binding strategy resolved for the
  composite parameter (typically a missing `Value` property on an inner VO).
- [`ZAO043`](../diagnostics/ZAO043.md) — `[Materialize(Factory)]` references a
  missing / non-static / non-public method.
- [`ZAO044`](../diagnostics/ZAO044.md) — Ambiguous discovery (e.g. multiple
  factory overloads with the same name).
- [`ZAO050`](../diagnostics/ZAO050.md) — Nullable composite runtime
  all-or-nothing check warning.
- [`ZAO051`](../diagnostics/ZAO051.md) — Factory parameter name doesn't match
  any SELECT column.
- [`ZAO052`](../diagnostics/ZAO052.md) — Recursive composite (composite of
  composite); deferred to v0.6+.
- [`ZAO063`](../diagnostics/ZAO063.md) — `[Param(Name = ...)]` is not
  supported on composite parameters.

## See also

- [`commands.md`](commands.md) — `[Command]` for INSERT/UPDATE/DELETE and
  scalar / identity returns.
- [`multi-result-set.md`](multi-result-set.md) — `[Query]` tuple-shaped
  multi-result returns.
- [`streaming.md`](streaming.md) — `IAsyncEnumerable<T>` for bounded-memory
  result iteration.
- [`stored-procedures.md`](stored-procedures.md) — `[StoredProcedure]` for
  procedure-driven calls and output parameters.
- [`flat-row.md`](flat-row.md) — single-row `Task<T?>` returns; the natural
  outer shape that holds a composite field.
- [`provider-quirks.md`](provider-quirks.md) — provider-specific decimal,
  identifier, and numeric handling.
