<h1 align="center">ZeroAlloc.ORM</h1>

<p align="center">Source-generator-based, NativeAOT-clean raw-SQL data access for .NET. Annotate <code>partial</code> methods with <code>[Query]</code> / <code>[Command]</code> / <code>[StoredProcedure]</code>; the generator emits typed parameter binding + materialization against <a href="https://github.com/MarcelRoozekrans/AdoNet.Async">AdoNet.Async</a>. Zero runtime reflection.</p>

> **Status:** Pre-release. v0.7 shipped (BenchmarkDotNet suite + ZA.Rest collision smoke + README polish + v1.0 public-API freeze via PublicApiAnalyzers). Authoritative design lives at [`docs/design/2026-05-30-v1.0-design.md`](docs/design/2026-05-30-v1.0-design.md). Working backlog at [`docs/plans/za-orm-backlog.md`](docs/plans/za-orm-backlog.md).

## What it is

A source-generator-driven data-access library that fills in the gap between two extremes adopters currently choose from:

- **EF Core** — full LINQ-to-SQL ORM, but its precompile-queries pipeline currently collides with co-resident source generators (e.g. ZA.Rest), blocking NativeAOT publish in template stacks like [ZeroAlloc.Templates](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates).
- **Hand-written ADO.NET** — works under AOT, but every repository becomes a hand-shaped tower of `CreateCommand` / `CreateParameter` / `ReadAsync` calls.

ZeroAlloc.ORM is the middle path: write the SQL string in an attribute, declare the partial method signature, let the source generator emit the ADO.NET pipeline. Zero runtime reflection, fully AOT-publishable, idiomatic with the rest of the ZeroAlloc ecosystem (consumes `AdoNet.Async`, dogfoods `ZeroAlloc.ValueObjects`, shares the convention catalog with `ZeroAlloc.Mapping`).

## Packages

| Package | Version | AOT | Description |
|---------|---------|-----|-------------|
| `ZeroAlloc.ORM` | [![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.ORM.svg)](https://www.nuget.org/packages/ZeroAlloc.ORM) | ✅ | Runtime extensions and exception types |
| `ZeroAlloc.ORM.Abstractions` | [![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.ORM.Abstractions.svg)](https://www.nuget.org/packages/ZeroAlloc.ORM.Abstractions) | ✅ | `[Query]` / `[Command]` / `[StoredProcedure]` / `[Materialize]` attributes |
| `ZeroAlloc.ORM.Generator` | [![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.ORM.Generator.svg)](https://www.nuget.org/packages/ZeroAlloc.ORM.Generator) | ✅ (build-time) | Roslyn incremental source generator |
| `ZeroAlloc.TypeConversions` | [![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.TypeConversions.svg)](https://www.nuget.org/packages/ZeroAlloc.TypeConversions) | ✅ (build-time) | Convention discovery catalog shared with ZA.Mapping |

## Quick Start

Every example below assumes a containing `partial class` that exposes an `IAsyncDbConnection` (from [AdoNet.Async](https://github.com/MarcelRoozekrans/AdoNet.Async)) — usually injected via primary constructor. The source generator emits the open / execute / close pipeline against that connection.

### 1. Single-row read

```csharp
[Query("SELECT id, name FROM customers WHERE id = @id")]
public partial Task<Customer?> GetCustomerAsync(int id, CancellationToken ct);

public sealed record Customer(int Id, string Name);
```

Positional record + matching SELECT column order = no mapping config. Nullable return type = empty result set yields `null`. See [docs/cookbook/multi-result-set.md](docs/cookbook/multi-result-set.md) for the head + lines tuple pattern.

### 2. Streaming with `IAsyncEnumerable`

```csharp
[Query("SELECT id, name FROM customers ORDER BY id")]
public partial IAsyncEnumerable<Customer> StreamCustomersAsync(
    [EnumeratorCancellation] CancellationToken ct);
```

Connection opens lazily on first `MoveNextAsync`, closes deterministically on `DisposeAsync` (including early `break` exits). More in [docs/cookbook/streaming.md](docs/cookbook/streaming.md).

### 3. Insert returning identity

```csharp
[Command(
    "INSERT INTO orders (customer_id, total) VALUES (@customerId, @total)",
    Kind = CommandKind.Identity)]
public partial Task<int> InsertOrderAsync(int customerId, decimal total, CancellationToken ct);
```

`CommandKind.Identity` appends the provider-aware identity suffix (`SCOPE_IDENTITY()` for SQL Server, `LAST_INSERT_ROWID()` for Sqlite, `RETURNING` for Postgres). More in [docs/cookbook/commands.md](docs/cookbook/commands.md).

### 4. Stored procedure with output parameters

```csharp
[StoredProcedure("usp_insert_order")]
public partial Task<(int rowsAffected, int newOrderId, decimal computedTotal)> InsertOrderSprocAsync(
    int customerId, CancellationToken ct);
```

The first tuple field is the result-set materialization (here: rows-affected). Subsequent named tuple fields map to `ParameterDirection.Output` SQL parameters by name. More in [docs/cookbook/stored-procedures.md](docs/cookbook/stored-procedures.md).

## Capabilities by milestone

### Added in v0.1

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

Deferred to later milestones: recursive composites (v1.0+, ZAO052 flags them today, tracked under v0.5-CLN3); nullable reference-type composite parameter binding (v0.5-CLN2 still open); TVPs / array parameters / `SqlBulkCopy` (out of v1.0 scope); provider routing of identity suffixes beyond Sqlite (v2); SQL Server integration fixture (v1.0 if adopter demand surfaces); Docusaurus website + DocFX API reference (v1.0-G2); cookbook polish pass (v1.0-G1); v0.5-CLN2 nullable RT composite binding; ZA.Telemetry collision smoke (v0.6-CLN1, blocked on upstream nullable-annotation fix).

### Added in v0.6

- **Postgres integration fixture (Testcontainers)** — `tests/ZeroAlloc.ORM.Integration.Tests/Postgres/` runs the full integration matrix (FlatRow, multi-result-set with real `IAsyncDbBatch`, streaming, stored procedures with `INOUT`/`OUT` params, `[Materialize(Factory)]` against `NUMERIC` columns, composites) against a real Postgres 16 container. Resolves the accumulated v0.3/v0.4/v0.5 deferrals: the runtime `IAsyncDbBatch` branch (v0.3-CLN3), stored-procedure round-trips (v0.4 placeholder), and `Money.FromStorage` against a real decimal provider (v0.5).

- **Diagnostics catalog audit** — every shipping ZAO code now has a dedicated reference page under [`docs/diagnostics/`](docs/diagnostics/) with trigger, fix recipe, code example, and related codes. A new `DiagnosticHelpLinkTests` suite enforces that every `DiagnosticDescriptor.HelpLinkUri` resolves to a real, non-empty markdown file — broken links can't be shipped. Positive/negative test pairs backfilled for ZAO001 and ZAO043. The catalog table (below) is the canonical adopter-facing index.

- **ZA.Telemetry observability cookbook recipe** — [`docs/cookbook/observability.md`](docs/cookbook/observability.md) shows the composition pattern at the consumer seam: a `partial class OrderRepository` annotated with both `[Query]` (ZA.ORM) and `[Instrument]` (ZA.Telemetry), with the two generators emitting independently. ZA.ORM ships **no built-in `ActivitySource`** — observability lives at the adopter boundary so the package graph stays minimal and consumers pick their own tracing stack. Collision smoke deferred to v0.6-CLN1 (blocked on upstream nullable-annotation fix in ZA.Telemetry's `InstrumentGenerator`).

- **v0.3-CLN1 perf cleanup — GetOrdinal hoisted once per column** — every column-name materialization path (DomainEntity, FlatRow column-name fallback, nullable composite) now emits `var __o_<Col> = __reader.GetOrdinal("<Col>");` ONCE before the materialization body and reuses the local in both the `IsDBNull` and `GetXxx` calls. Eliminates the double-lookup in the hot row-materialization loop.

- **v0.5-CLN5 fix — PR-title lint workflow** — `.github/workflows/pr-title-lint.yml` enforces conventional-commit prefixes (`feat:`, `fix:`, `perf:`, `refactor:`, `docs:`, `test:`, `ci:`, `chore:`, ...) on every PR. Prevents the v0.5 release CHANGELOG hole where `feat:`-less merges silently dropped from release-please's commit aggregation.

### Added in v0.7

- **BenchmarkDotNet suite** — `tests/ZeroAlloc.ORM.Benchmarks/` ships a comparative micro-benchmark harness with 4 workloads (single-row read, multi-row read, head + lines multi-result, insert) × 3 baselines (hand-written ADO.NET, Dapper.AOT, ZeroAlloc.ORM) × 2 backends (Sqlite in-memory and Postgres via Testcontainers). First Sqlite capture (Windows 11, .NET 10.0.300) lives in [`docs/benchmarks/v0.7.0-sqlite-results.md`](docs/benchmarks/v0.7.0-sqlite-results.md): ZA.ORM sits within 5% of hand-written ADO.NET on single-row reads and matches its allocation profile to ~0.5% on 1000-row reads; multi-result-set has a 30% gap that's the next v1.0+ target.

- **ZA.Rest collision smoke (v1.0 release gate)** — `tests/ZeroAlloc.ORM.GeneratorCollision.AotSmoke/` composes `[Query]` (ZA.ORM) and `[Route]`/`[Query]` (ZA.Rest) in a single AOT-publishable consumer. Wired into `.github/workflows/collision-smoke.yml`, so every PR proves the two source generators co-exist and the resulting binary AOT-publishes cleanly. Discovery during Phase B: both libraries ship a `QueryAttribute`; the collision is resolved cleanly via file-scoped `using` aliases at the call site. **This is the v1.0 release gate** — if collision-smoke ever breaks, v1.0 doesn't ship.

- **README + Quick Start polish (Phase C)** — packages table now carries an AOT compatibility column (every shipping package marked ✅), four canonical Quick Start snippets at the top of the doc (single-row read, streaming, insert-returning-identity, stored procedure with output params), and a dedicated **NativeAOT compatibility** section calling out the AOT smoke + collision smoke gates and pointing at the benchmark suite for performance numbers.

- **v1.0 public-API surface freeze** — `Microsoft.CodeAnalysis.PublicApiAnalyzers` is wired across `ZeroAlloc.ORM`, `ZeroAlloc.ORM.Abstractions`, and `ZeroAlloc.TypeConversions`, with `PublicAPI.Shipped.txt` baselined at **103 entries across 16 public types**. Any accidental addition / change / removal of a public member now breaks `dotnet build`. The surface lock holds until v1.0 ships, and any v1.x evolution must go through the additive `PublicAPI.Unshipped.txt` path with explicit reviewer sign-off.

## NativeAOT compatibility

ZeroAlloc.ORM is fully `NativeAOT`-compatible by design:

- **Zero reflection at runtime.** Source-generator-based emit produces compile-time-known materialization code; no `Activator.CreateInstance`, no `Type.GetMethod`, no `Expression.Compile`.
- **Globally-qualified type references** in every emitted line — AOT publishing trims correctly regardless of consumer `using` directives.
- **Trimming-safe.** No `[DynamicallyAccessedMembers]` requirements on consumer types.
- **CI gated.** Every PR runs:
  - `tests/ZeroAlloc.ORM.AotSmoke/` — single-generator AOT publish.
  - `tests/ZeroAlloc.ORM.GeneratorCollision.AotSmoke/` — composition with `ZeroAlloc.Rest.Generator` (the v1.0 release gate).
- **Performance.** ZA.ORM is within 5% of hand-written ADO.NET on single-row reads and matches its allocation profile on multi-row reads; see [docs/benchmarks/v0.7.0-sqlite-results.md](docs/benchmarks/v0.7.0-sqlite-results.md) for the full comparison against hand-written ADO.NET and Dapper.AOT.

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

## Cookbook recipes

Adopter-facing recipes for the eight canonical patterns shipped in v1.0. Each page is paste-into-fresh-project quality with provider notes and diagnostics cross-links.

| Recipe | Description |
|--------|-------------|
| [flat-row.md](docs/cookbook/flat-row.md) | Single-row reads → positional record (`Task<T?>` / `Task<T>`). |
| [multi-result-set.md](docs/cookbook/multi-result-set.md) | `(head, lines)` tuple returns; `BatchMode.Auto` / `Always` / `Never`. |
| [streaming.md](docs/cookbook/streaming.md) | `IAsyncEnumerable<T>` for unbounded result sets. |
| [commands.md](docs/cookbook/commands.md) | `[Command]` — `NonQuery` / `Scalar` / `Identity`. |
| [stored-procedures.md](docs/cookbook/stored-procedures.md) | `[StoredProcedure]` with output parameters + multi-result-set sprocs. |
| [composites.md](docs/cookbook/composites.md) | Multi-column composites (`Money`) + `[Materialize(Factory)]`. |
| [observability.md](docs/cookbook/observability.md) | ZA.Telemetry composition (`[Instrument]` + `[Trace]` + `[Count]` + `[Histogram]`). |
| [provider-quirks.md](docs/cookbook/provider-quirks.md) | Sqlite / PostgreSQL / SQL Server / MySQL differences. |

## Design + roadmap

- **Design:** [`docs/design/2026-05-30-v1.0-design.md`](docs/design/2026-05-30-v1.0-design.md) — 5-section v1.0 design covering architecture, generator surface, convention discovery, diagnostics, test strategy, milestones.
- **Backlog:** [`docs/plans/za-orm-backlog.md`](docs/plans/za-orm-backlog.md) — priority-banded task list. New findings during implementation get appended.
- **Ecosystem context:** sits alongside [ZeroAlloc.Mediator](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator), [ZeroAlloc.Mapping](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping), [ZeroAlloc.ValueObjects](https://github.com/ZeroAlloc-Net/ZeroAlloc.ValueObjects), [ZeroAlloc.Validation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation). Substrate is [AdoNet.Async](https://github.com/MarcelRoozekrans/AdoNet.Async) (AOT-compatible since v1.x).

## License

[MIT](LICENSE)
