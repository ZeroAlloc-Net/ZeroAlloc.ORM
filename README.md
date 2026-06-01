<h1 align="center">ZeroAlloc.ORM</h1>

<p align="center">Source-generator-based, NativeAOT-clean raw-SQL data access for .NET. Annotate <code>partial</code> methods with <code>[Query]</code> / <code>[Command]</code> / <code>[StoredProcedure]</code>; the generator emits typed parameter binding + materialization against <a href="https://github.com/MarcelRoozekrans/AdoNet.Async">AdoNet.Async</a>. Zero runtime reflection.</p>

> **Status:** Pre-release. v0.5 shipped (multi-column composites + `[Materialize(Factory)]` + nullable composite handling). Authoritative design lives at [`docs/design/2026-05-30-v1.0-design.md`](docs/design/2026-05-30-v1.0-design.md). Working backlog at [`docs/plans/za-orm-backlog.md`](docs/plans/za-orm-backlog.md).

## What it is

A source-generator-driven data-access library that fills in the gap between two extremes adopters currently choose from:

- **EF Core** — full LINQ-to-SQL ORM, but its precompile-queries pipeline currently collides with co-resident source generators (e.g. ZA.Rest), blocking NativeAOT publish in template stacks like [ZeroAlloc.Templates](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates).
- **Hand-written ADO.NET** — works under AOT, but every repository becomes a hand-shaped tower of `CreateCommand` / `CreateParameter` / `ReadAsync` calls.

ZeroAlloc.ORM is the middle path: write the SQL string in an attribute, declare the partial method signature, let the source generator emit the ADO.NET pipeline. Zero runtime reflection, fully AOT-publishable, idiomatic with the rest of the ZeroAlloc ecosystem (consumes `AdoNet.Async`, dogfoods `ZeroAlloc.ValueObjects`, shares the convention catalog with `ZeroAlloc.Mapping`).

## Packages

| Package | Description | NativeAOT |
|---------|-------------|---|
| **ZeroAlloc.ORM** | Runtime helpers + `ActivitySource` for observability. Depends on AdoNet.Async. | ✅ |
| **ZeroAlloc.ORM.Abstractions** | Public attribute surface (`[Query]`, `[Command]`, `[StoredProcedure]`, `[Param]`, `[StoreAsString]`, `[Materialize]`) + exception types. | ✅ |
| **ZeroAlloc.ORM.Generator** | Roslyn incremental source generator. Build-time only. | N/A |
| **ZeroAlloc.TypeConversions** | Shared convention-discovery catalog (value-objects, enums, composites). Build-time only. | N/A |

## Quick Start

### Scalar query

```csharp
using System.Data.Async;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.ORM;

public sealed partial class Repo(IAsyncDbConnection connection)
{
    [Query("SELECT count(*) FROM Orders")]
    public partial Task<int> CountOrdersAsync(CancellationToken ct);
}
```

The source generator emits the open / execute / close pipeline against AdoNet.Async's `IAsyncDbConnection`. Zero runtime reflection; the emit composes through `global::`-qualified identifiers so it doesn't care about the consumer's `using` directives.

### Row materialization (FlatRow)

```csharp
public sealed record OrderRow(int Id, int CustomerId, decimal Total);

public sealed partial class OrderRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
    public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
}
```

Positional record + matching SELECT column order = no mapping config. Nullable return = empty result set yields `null`.

### Available in v0.1

- `[Query]` with scalar (`Task<int>`, `Task<T?>`) and FlatRow (`Task<TRow?>`) return shapes.
- 14 primitive types in parameter binding (int / long / short / byte / bool / decimal / double / float / string / Guid / DateTime / DateTimeOffset / TimeSpan / byte[]) + nullable variants.
- `[Param(Name = "...")]` SQL-side parameter name override.
- Compile-time diagnostics (ZAO001–ZAO009 + informational ZAO020–ZAO022) for signature contract violations.
- NativeAOT-clean publish (verified by `aot-smoke` CI gate).

### Added in v0.2

- **Value-object columns** — types annotated with `[ValueObject]` from [ZeroAlloc.ValueObjects](https://github.com/ZeroAlloc-Net/ZeroAlloc.ValueObjects) (with a static `From` factory and a `Value` property) bind through their underlying primitive. Parameters unwrap to `Value`; reads wrap via `T.From(primitive)`.

  ```csharp
  [ValueObject]
  public readonly partial struct CustomerId
  {
      public int Value { get; }
      public CustomerId(int value) { Value = value; }
      public static CustomerId From(int value) => new(value);
  }

  public sealed record CustomerRow(CustomerId Id, string Name);

  public sealed partial class CustomerRepo(IAsyncDbConnection connection)
  {
      [Query("SELECT Id, Name FROM Customers WHERE Id = @id")]
      public partial Task<CustomerRow?> GetAsync(CustomerId id, CancellationToken ct);
  }
  ```

- **Enums (default int round-trip)** — any `enum` parameter or column binds as its underlying integer (`reader.GetInt32` + cast on read; cast to underlying primitive on bind).
- **Enums (string round-trip)** — annotate the `enum` type with `[StoreAsString]` to round-trip as the member name (`reader.GetString` + `Enum.Parse<T>` on read; member-name bind).
- **Domain-entity classes** — plain `class` types with a single multi-arg public ctor materialize via column-name-keyed reads (`__reader.GetOrdinal("ColumnName")`). SELECT column order is irrelevant; each ctor parameter resolves to its matching column by name. Records keep the positional `FlatRow` path.
- **Single-arg record discovery + static `From` factory discovery** — wrappers without `[ValueObject]` still resolve when ConventionDiscovery can find an obvious construction strategy.
- **New diagnostics ZAO040–ZAO044** — materialization-side failures (no construction strategy, conflicting strategies, unresolved ctor parameters, etc.) surface at build time with focused messages.

### Added in v0.3

- **Multi-result-set tuples (head + lines pattern)** — tuple return types map each tuple element to one `;`-separated SQL statement. Collapses the 1+N round-trip into a single command. Tuple element kinds: scalar (`int`, value-object, enum), row (`record` / single-ctor class), or list (`List<T>` / `IReadOnlyList<T>` / `IEnumerable<T>` / ...). See [`docs/cookbook/multi-result-set.md`](docs/cookbook/multi-result-set.md).

  ```csharp
  public sealed record OrderRow(int Id, int CustomerId, decimal Total);
  public sealed record OrderLineRow(int Id, int OrderId, string Sku, int Qty);

  public sealed partial class OrderRepo(IAsyncDbConnection connection)
  {
      [Query("""
          SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id;
          SELECT Id, OrderId, Sku, Qty FROM OrderLines WHERE OrderId = @id;
          """)]
      public partial Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?> GetWithLinesAsync(
          int id, CancellationToken ct);
  }
  ```

- **Batch dispatch (`BatchMode.Auto` / `Always` / `Never`)** — `[Query(Batch = ...)]` picks how multi-statement SQL reaches the provider. `Auto` (default) branches at runtime on `connection.CanCreateBatch`: providers exposing `IAsyncDbBatch` get the pipelined path; everyone else falls back to a single command with `;`-joined SQL and `NextResultAsync`. Both paths produce the same materialized tuple.

- **`IAsyncEnumerable<T>` streaming** — partial methods returning `IAsyncEnumerable<T>` with a `[EnumeratorCancellation] CancellationToken` parameter emit an async iterator that materializes rows lazily. Connection opens on first `MoveNextAsync`, closes deterministically on `DisposeAsync` (including early `break` exits). See [`docs/cookbook/streaming.md`](docs/cookbook/streaming.md).

  ```csharp
  public sealed partial class OrderRepo(IAsyncDbConnection connection)
  {
      [Query("SELECT Id, CustomerId, Total FROM Orders ORDER BY Id")]
      public partial IAsyncEnumerable<OrderRow> StreamAllAsync(
          [EnumeratorCancellation] CancellationToken ct);
  }
  ```

- **New diagnostics ZAO032 / ZAO033** — arity mismatch between a tuple return and the number of `;`-separated SQL statements. ZAO032 fires when the tuple has more elements than statements; ZAO033 fires when it has fewer. Both at build time, so the wrong shape never ships.

### Added in v0.4

- **`[Command]` attribute** — non-`SELECT` SQL with three result-shape modes selected via `CommandKind`. `NonQuery` returns rows-affected (`int`), `Scalar` returns the first column of the first row (any materialization-eligible type), `Identity` returns the inserted identity value through provider-aware suffixes (`SCOPE_IDENTITY()` for SQL Server, `LAST_INSERT_ROWID()` for Sqlite, `RETURNING` for Postgres). See [`docs/cookbook/commands.md`](docs/cookbook/commands.md).

  ```csharp
  public sealed partial class OrderRepo(IAsyncDbConnection connection)
  {
      [Command("UPDATE Orders SET Total = @total WHERE Id = @id", Kind = CommandKind.NonQuery)]
      public partial Task<int> UpdateTotalAsync(int id, decimal total, CancellationToken ct);

      [Command("SELECT COUNT(*) FROM Orders WHERE CustomerId = @customerId", Kind = CommandKind.Scalar)]
      public partial Task<int> CountByCustomerAsync(int customerId, CancellationToken ct);

      [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@customerId, @total)", Kind = CommandKind.Identity)]
      public partial Task<int> InsertAsync(int customerId, decimal total, CancellationToken ct);
  }
  ```

- **`[StoredProcedure]` attribute** — emit `CommandType = StoredProcedure` with the procedure name as `CommandText`. Result shapes mirror `[Query]`: scalar, single-row, list, multi-result-set tuples. Parameters bind by name. See [`docs/cookbook/stored-procedures.md`](docs/cookbook/stored-procedures.md).

  ```csharp
  public sealed partial class OrderRepo(IAsyncDbConnection connection)
  {
      [StoredProcedure("usp_GetOrderWithLines")]
      public partial Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?> GetWithLinesAsync(
          int orderId, CancellationToken ct);
  }
  ```

- **Named-tuple output parameters** — on `[StoredProcedure]` methods, tuple return fields beyond the first map to `ParameterDirection.Output` SQL parameters by name. Output values are copied back into the tuple after execution. The first tuple field is still the result-set materialization (scalar, row, or list); subsequent named fields are output params.

  ```csharp
  public sealed partial class OrderRepo(IAsyncDbConnection connection)
  {
      [StoredProcedure("usp_CreateOrder")]
      public partial Task<(int rowsAffected, int newOrderId, decimal computedTotal)> CreateAsync(
          int customerId, CancellationToken ct);
  }
  ```

  The generator emits `Direction = ParameterDirection.Output` on `@newOrderId` and `@computedTotal`, executes the proc, then reads the output values back into the returned tuple.

- **New diagnostics ZAO060 (reserved) / ZAO061 / ZAO062** — sproc-side compile-time guardrails. ZAO060 is reserved for a future `out`/`ref`-on-async check (the C# compiler already rejects `out`/`ref` on async-returning partials, so the dedicated ZAO060 message is unnecessary at the source-generator layer for now). ZAO061 fires on `[StoredProcedure("")]` (empty procedure name). ZAO062 fires when a named-tuple output-parameter field doesn't appear as a method parameter that could carry the output back to SQL — the name must match an `@param` the proc declares.

### Added in v0.5

- **Multi-column composites (Money pattern)** — declare a positional-ctor type like `Money(decimal Amount, string Currency)` and the generator unpacks it into N SQL columns automatically. Each ctor parameter resolves through the existing convention pipeline (primitive / enum / value-object / single-arg-ctor / static-factory). Nested composites flatten transparently — `record OrderRow(int Id, Money Total)` reads as 3 columns. See [`docs/cookbook/composites.md`](docs/cookbook/composites.md).

  ```csharp
  public readonly record struct Money(decimal Amount, string Currency);

  public sealed partial class OrderRepo(IAsyncDbConnection connection)
  {
      [Query("SELECT Id, Amount, Currency FROM Orders WHERE Id = @id")]
      public partial Task<OrderRow?> GetAsync(int id, CancellationToken ct);

      [Command("UPDATE Orders SET Amount = @total_Amount, Currency = @total_Currency WHERE Id = @id", Kind = CommandKind.NonQuery)]
      public partial Task<int> UpdateTotalAsync(int id, Money total, CancellationToken ct);
  }
  ```

  Composite method parameters bind via positional unpacking: `Money total` → `@total_Amount` + `@total_Currency`. Naming uses the parameter name + `_` + ctor-parameter name; the SQL author picks how the columns line up.

- **Nullable composites (all-or-nothing)** — `Money? Total` means "all composite columns NULL → `null`; any composite column NULL while others have values → `ZeroAllocOrmMaterializationException` at runtime." Compile-time warning **ZAO050** fires on each nullable-composite materialization site to flag that the partial-null case is undetectable at compile time and is by-design a runtime throw.

- **`[Materialize(Factory = "...")]`** — explicit factory resolution for cases where the SQL shape doesn't match the C# ctor. The named `static` factory's parameter list maps to columns by name (positional fallback when SQL column names aren't available). Canonical use case: Sqlite stores `decimal` as TEXT, so route through a `Money.FromText(string amountText, string currency)` factory.

  ```csharp
  [Materialize(Factory = nameof(FromText))]
  public readonly record struct Money(decimal Amount, string Currency)
  {
      public static Money FromText(string Amount, string Currency) =>
          new(decimal.Parse(Amount, CultureInfo.InvariantCulture), Currency);
  }
  ```

  Diagnostics: **ZAO043** if the named factory doesn't exist; **ZAO044** if discovery is ambiguous; **ZAO051** if the factory's parameter list cannot be reconciled with the available columns.

- **New diagnostics ZAO050 / ZAO051 / ZAO052 / ZAO063** — composite + factory + sproc-batch guardrails. ZAO050 (nullable-composite partial-null runtime-only check, see above). ZAO051 (factory parameter list unresolved). ZAO052 (recursive composite — a composite ctor parameter that is itself another composite — explicitly deferred to v0.6+ with a clear error). ZAO063 (informational: `[StoredProcedure(Batch = ...)]` with a non-default value is silently ignored — sprocs encapsulate their own batching semantics).

Deferred to later milestones: recursive composites (v0.6+, ZAO052 flags them today); ActivitySource / built-in observability (v0.6 via ZA.Telemetry composition); Postgres / SQL Server integration fixtures including stored-procedure round-trips (v0.6); TVPs / array parameters / `SqlBulkCopy` (out of v1.0 scope); provider routing of identity suffixes beyond Sqlite (v2).

## Diagnostics catalog

ZeroAlloc.ORM ships a structured catalog of compile-time diagnostics. Every code has a dedicated reference page in [`docs/diagnostics/`](docs/diagnostics/) — the IDE help link on each diagnostic resolves to its page directly.

| Code | Severity | Trigger | Link |
|------|----------|---------|------|
| ZAO001 | Error | Annotated method must be partial | [ZAO001](docs/diagnostics/ZAO001.md) |
| ZAO002 | Error | Unsupported return type | [ZAO002](docs/diagnostics/ZAO002.md) |
| ZAO003 | Error | No `IAsyncDbConnection` found on containing type | [ZAO003](docs/diagnostics/ZAO003.md) |
| ZAO004 | Error | Containing type must be partial | [ZAO004](docs/diagnostics/ZAO004.md) |
| ZAO005 | Error | Multiple ORM attributes on one method | [ZAO005](docs/diagnostics/ZAO005.md) |
| ZAO006 | Warning | Method has multiple `CancellationToken` parameters | [ZAO006](docs/diagnostics/ZAO006.md) |
| ZAO007 | Error | `IAsyncEnumerable<T>` return without `[EnumeratorCancellation]` | [ZAO007](docs/diagnostics/ZAO007.md) |
| ZAO008 | Error | Multi-statement SQL with single-result return type | [ZAO008](docs/diagnostics/ZAO008.md) |
| ZAO009 | Warning | Redundant `async` keyword on generated partial | [ZAO009](docs/diagnostics/ZAO009.md) |
| ZAO020 | Info | `[Query](FromResource = true)` not yet implemented | [ZAO020](docs/diagnostics/ZAO020.md) |
| ZAO022 | Info | Return type shape not yet supported | [ZAO022](docs/diagnostics/ZAO022.md) |
| ZAO032 | Error | Tuple arity exceeds SQL statement count | [ZAO032](docs/diagnostics/ZAO032.md) |
| ZAO033 | Error | SQL statement count exceeds tuple arity | [ZAO033](docs/diagnostics/ZAO033.md) |
| ZAO040 | Error | No construction strategy resolved for type | [ZAO040](docs/diagnostics/ZAO040.md) |
| ZAO041 | Error | No binding strategy resolved for parameter | [ZAO041](docs/diagnostics/ZAO041.md) |
| ZAO042 | Error | `[StoreAsString]` requires an enum type | [ZAO042](docs/diagnostics/ZAO042.md) |
| ZAO043 | Error | `[Materialize(Factory)]` references missing method | [ZAO043](docs/diagnostics/ZAO043.md) |
| ZAO044 | Error | Ambiguous convention discovery | [ZAO044](docs/diagnostics/ZAO044.md) |
| ZAO050 | Warning | Nullable composite type requires runtime all-or-nothing check | [ZAO050](docs/diagnostics/ZAO050.md) |
| ZAO051 | Error | Factory parameter does not match any SELECT column | [ZAO051](docs/diagnostics/ZAO051.md) |
| ZAO052 | Error | Recursive composite types are not supported | [ZAO052](docs/diagnostics/ZAO052.md) |
| ZAO060 | Error | `[StoredProcedure]` async method has out/ref parameter (reserved) | [ZAO060](docs/diagnostics/ZAO060.md) |
| ZAO061 | Error | `[StoredProcedure]` name is empty | [ZAO061](docs/diagnostics/ZAO061.md) |
| ZAO062 | Warning | Named-tuple field does not match any parameter | [ZAO062](docs/diagnostics/ZAO062.md) |
| ZAO063 | Error | `[Param(Name = ...)]` override is not supported on composite parameters | [ZAO063](docs/diagnostics/ZAO063.md) |

A unit test (`DiagnosticHelpLinkTests`) enforces that every `DiagnosticDescriptor.HelpLinkUri` resolves to a real, non-empty markdown page under `docs/diagnostics/` — broken links can't be shipped.

## Design + roadmap

- **Design:** [`docs/design/2026-05-30-v1.0-design.md`](docs/design/2026-05-30-v1.0-design.md) — 5-section v1.0 design covering architecture, generator surface, convention discovery, diagnostics, test strategy, milestones.
- **Backlog:** [`docs/plans/za-orm-backlog.md`](docs/plans/za-orm-backlog.md) — priority-banded task list. New findings during implementation get appended.
- **Ecosystem context:** sits alongside [ZeroAlloc.Mediator](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator), [ZeroAlloc.Mapping](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping), [ZeroAlloc.ValueObjects](https://github.com/ZeroAlloc-Net/ZeroAlloc.ValueObjects), [ZeroAlloc.Validation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation). Substrate is [AdoNet.Async](https://github.com/MarcelRoozekrans/AdoNet.Async) (AOT-compatible since v1.x).

## License

[MIT](LICENSE)
