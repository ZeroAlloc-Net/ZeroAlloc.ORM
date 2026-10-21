# ZeroAlloc.ORM reference

The capability reference for ZeroAlloc.ORM 2.x: packages, every supported return and parameter shape,
migrations, NativeAOT guarantees, diagnostics and the documentation index. For the overview and a
first example, see the [README](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/README.md).

## Install

```bash
dotnet add package ZeroAlloc.ORM
dotnet add package ZeroAlloc.ORM.Generator
```

Reference the generator as a build-time analyzer so it does not flow to downstream projects:

```xml
<PackageReference Include="ZeroAlloc.ORM.Generator" Version="2.*" PrivateAssets="all" />
```

Then add your ADO.NET provider, such as `Microsoft.Data.Sqlite`, `Npgsql` or `Microsoft.Data.SqlClient`, and wrap its connection with AdoNet.Async's `AsAsync()`.

## Packages

| Package | Description |
|---------|-------------|
| [`ZeroAlloc.ORM`](https://www.nuget.org/packages/ZeroAlloc.ORM) | Runtime: `MigrationRunner` with the SQLite, PostgreSQL and SQL Server migration dialects, and the `ZeroAllocOrm*Exception` types. Depends on `ZeroAlloc.ORM.Abstractions` and AdoNet.Async. |
| [`ZeroAlloc.ORM.Abstractions`](https://www.nuget.org/packages/ZeroAlloc.ORM.Abstractions) | The attributes and enums you annotate with: `[Query]`, `[Command]`, `[StoredProcedure]`, `[Param]`, `[Materialize]`, `[StoreAsString]`, `[Dialect]`, `CommandKind`, `BatchMode`, `SqlDialect`. |
| [`ZeroAlloc.ORM.Generator`](https://www.nuget.org/packages/ZeroAlloc.ORM.Generator) | Roslyn incremental source generator and the `ZAO` diagnostics. Build-time only; bundles `ZeroAlloc.TypeConversions`. |
| [`ZeroAlloc.TypeConversions`](https://www.nuget.org/packages/ZeroAlloc.TypeConversions) | Build-time type-conversion and convention-discovery catalog used by the generator. You do not reference it directly. |

The runtime packages target `net10.0` and set `IsAotCompatible`. The generator and TypeConversions target `netstandard2.0` and only run inside the compiler.

## Repository shapes

Every repository is a `partial` type that exposes an `IAsyncDbConnection`, usually through its primary constructor:

```csharp
using System.Collections.Generic;
using System.Data.Async;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.ORM;

public sealed record Customer(int Id, string Name);
public sealed record OrderRow(int Id, int CustomerId, decimal Total);
public sealed record NewOrder(int CustomerId, decimal Total);

public sealed partial class OrderRepository(IAsyncDbConnection connection)
{
    // Single row; a nullable return yields null on an empty result set.
    [Query("SELECT Id, Name FROM Customers WHERE Id = @id")]
    public partial Task<Customer?> GetCustomerAsync(int id, CancellationToken ct);

    // Many rows, materialized eagerly. List<T>, IList<T> and IReadOnlyList<T> all work.
    [Query("SELECT Id, CustomerId, Total FROM Orders WHERE CustomerId = @customerId")]
    public partial Task<IReadOnlyList<OrderRow>> GetOrdersAsync(int customerId, CancellationToken ct);

    // Streaming: the connection opens on the first MoveNextAsync and closes on DisposeAsync.
    [Query("SELECT Id, CustomerId, Total FROM Orders ORDER BY Id")]
    public partial IAsyncEnumerable<OrderRow> StreamOrdersAsync([EnumeratorCancellation] CancellationToken ct);

    // Insert returning the identity; the SQL carries the provider's identity clause.
    [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@customerId, @total) RETURNING Id",
        Kind = CommandKind.Identity)]
    public partial Task<int> InsertOrderAsync(int customerId, decimal total, CancellationToken ct);

    // Join a caller's transaction by declaring an IAsyncDbTransaction parameter.
    [Command("UPDATE Orders SET Total = @total WHERE Id = @id")]
    public partial Task<int> UpdateTotalAsync(int id, decimal total, IAsyncDbTransaction tx, CancellationToken ct);

    // Chunked multi-row INSERT; @CustomerId and @Total are NewOrder properties.
    [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)",
        Kind = CommandKind.BulkInsert)]
    public partial Task<int> InsertOrdersAsync(IReadOnlyList<NewOrder> orders, CancellationToken ct);

    // Stored procedure with an output parameter: the NewOrderId tuple field matches the
    // newOrderId parameter, so that parameter binds as ParameterDirection.Output and its value
    // comes back in the tuple. Result matches no parameter, so it is the result-set row.
    [StoredProcedure("usp_InsertOrder")]
    public partial Task<(OrderRow Result, int NewOrderId)> InsertViaProcedureAsync(
        int customerId, int newOrderId, CancellationToken ct);
}
```

Every `Task<T>` return above can be declared as `ValueTask<T>` instead; a call that completes synchronously then allocates no `Task`.

The argument passed for an output parameter is not sent. Mark it `[Param(Direction = ParameterDirection.InputOutput)]` when the procedure reads its initial value, or `ParameterDirection.ReturnValue` to read a SQL Server `RETURN` value. See [stored-procedures.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/cookbook/stored-procedures.md).

## Features

**Return shapes**

- Scalars (`Task<int>`, `Task<T?>`), single rows (`Task<TRow?>`), lists (`List<T>`, `IList<T>`, `IReadOnlyList<T>`) and streams (`IAsyncEnumerable<T>`); every `Task<T>` shape also has a `ValueTask<T>` form.
- Multi-result-set tuples, such as `Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?>`, map one tuple element to each `;`-separated statement. `[Query(Batch = BatchMode.Auto)]`, the default, uses `IAsyncDbBatch` when the provider supports it and falls back to one command with `NextResultAsync`. Tuple/statement arity mismatches are build errors (ZAO032, ZAO033).

**Parameters and columns**

- Primitives: `int`, `long`, `short`, `byte`, `bool`, `decimal`, `double`, `float`, `string`, `Guid`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `DateOnly`, `TimeOnly`, `byte[]`, and their nullable forms.
- PostgreSQL arrays: `T[]`, `List<T>` and `IReadOnlyList<T>` of `string`, `int`, `long` or `Guid` bind as parameters (for `= ANY(@ids)`) and read as columns, including as members of composites and bulk-insert rows. `null` binds `NULL`. They need Npgsql referenced, otherwise ZAO082. See [provider quirks](cookbook/provider-quirks.md#postgresql-arrays).
- PostgreSQL `jsonb`: mark a `string` or `string?` parameter, record or constructor parameter, composite member or bulk-insert row property with `[Json]` and it binds as `jsonb`, so the SQL needs no `::jsonb` cast. `null` binds `NULL`. Columns read as plain strings. `[Json]` needs Npgsql referenced (ZAO082) and a string (ZAO083). See [provider quirks](cookbook/provider-quirks.md#postgresql-jsonb).
- Enums round-trip as their underlying integer, or as the member name with `[StoreAsString]`.
- Value objects (`[ValueObject]` from [ZeroAlloc.ValueObjects](https://github.com/ZeroAlloc-Net/ZeroAlloc.ValueObjects), or any type with a single-argument constructor or static `From` factory) bind through their primitive.
- Multi-column composites such as `Money(decimal Amount, string Currency)` unpack into several columns; a parameter `Money total` binds `@total_Amount` and `@total_Currency`. Composites of composites are not supported yet (ZAO052).
- `[Param]` overrides the SQL-side `Name` and sets `DbType`, `Size`, `Precision`, `Scale` and `Direction`.

**Materialization**

- Positional records materialize by column position. Classes with one public multi-argument constructor materialize by column name, so SELECT order does not matter.
- `[Materialize(Factory = nameof(...))]` routes rows through a static factory when the SQL shape differs from the constructor, for example SQLite storing `decimal` as text.
- A `NULL` in a non-nullable column throws `ZeroAllocOrmMaterializationException`.

**Commands**

- `CommandKind.NonQuery` returns rows affected, `Scalar` returns the first column of the first row, `Identity` returns the inserted identity, and `BulkInsert` emits chunked multi-row `INSERT ... VALUES` returning the summed row count or the inserted identities.

**Stored procedures**

- Result rows, lists and multi-result sets, output and input-output parameters through named tuple fields, and SQL Server `RETURN` values.

**Transactions and dialects**

- An `IAsyncDbTransaction` parameter enlists the command in the caller's transaction (ZAO080 warns on more than one).
- `[Dialect(SqlDialect.SqlServer | PostgreSql | Sqlite | MySql)]` on a repository or the assembly enables provider-specific compile-time checks: ZAO015 for a `[Param(DbType)]` the provider rejects, ZAO065 for an unscaled `decimal` output on SQL Server, and ZAO069 for a return value off SQL Server. See [provider-quirks.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/cookbook/provider-quirks.md).

**Migrations**

`MigrationRunner` applies embedded `.sql` migrations in order, each in its own transaction, and records them in `__zaorm_migrations` per source, so an application and a library can each number from 1 in one database. It ships with `SqliteMigrationDialect`, `PostgresMigrationDialect` (`pg_advisory_lock`) and `SqlServerMigrationDialect` (`sp_getapplock`); the last two make concurrent startup across instances safe.

```csharp
using Npgsql;
using System.Data.Async;
using System.Data.Async.Adapters;
using ZeroAlloc.ORM.Migrations;

IAsyncDbConnection conn = new NpgsqlConnection(connString).AsAsync();
await conn.OpenAsync(ct);

var runner = new MigrationRunner(
    conn,
    new EmbeddedResourceMigrationSource(typeof(Program).Assembly),
    new PostgresMigrationDialect());   // or SqliteMigrationDialect / SqlServerMigrationDialect

IReadOnlyList<Migration> applied = await runner.RunAsync(ct);
```

`PostgresMigrationDialect(lockKey)` and `SqlServerMigrationDialect(lockName)` take a custom lock key or resource name. See [migrations.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/cookbook/migrations.md).

## NativeAOT compatibility

- **No runtime reflection.** The generator emits compile-time-known binding and materialization code; no `Activator.CreateInstance`, `Type.GetMethod` or `Expression.Compile`.
- **Globally-qualified type references** in every emitted line, so trimming is correct regardless of the consumer's `using` directives, and no `[DynamicallyAccessedMembers]` requirements on your types.
- **CI-gated on every PR:**
  - [`aot-smoke.yml`](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/.github/workflows/aot-smoke.yml) publishes `tests/ZeroAlloc.ORM.AotSmoke` with `PublishAot=true` and runs it.
  - [`collision-smoke.yml`](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/.github/workflows/collision-smoke.yml) AOT-publishes two consumers that combine ZA.ORM with another source generator: `tests/ZeroAlloc.ORM.GeneratorCollision.AotSmoke` with ZeroAlloc.Rest, whose `[Query]` attribute shares the name and is disambiguated with a file-scoped `using` alias, and `tests/ZeroAlloc.ORM.TelemetryCollision.AotSmoke` with ZeroAlloc.Telemetry's `[Instrument]` proxy wrapping a ZA.ORM repository.

## Performance

BenchmarkDotNet compares ZA.ORM with hand-written ADO.NET and Dapper.AOT, captured at 2.4.0 on 2026-10-21 on an otherwise idle machine. Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published. Time is the ratio against hand-written ADO.NET, from three launches.

| Workload | SQLite: time | SQLite: allocation | PostgreSQL: time | PostgreSQL: allocation |
|---|---:|---:|---:|---:|
| Single-row read | 1.06× | 1,568 B vs 1,232 B, +27% | 1.03× | +24% |
| 1000-row read | 1.06× | 88,592 B vs 88,200 B, +0.4% | 1.01× | +2% |
| Head + lines multi-result | 1.09× | 3,400 B vs 2,680 B, +27% | 1.00× | +56% |
| Single insert | 1.20× | 1,856 B vs 1,208 B, +54% | 0.98× | +7% |

Full tables, per-launch spreads and methodology: [SQLite](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/benchmarks/v2.x-sqlite-results.md) and [PostgreSQL](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/benchmarks/v2.x-postgres-results.md).

## Diagnostics

The generator reports signature, binding, materialization, stored-procedure, `BulkInsert` and transaction problems at build time, from `ZAO001` to `ZAO082`. Every code has a page under [docs/diagnostics/](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/tree/main/docs/diagnostics), and the IDE help link on each diagnostic opens it. The canonical index of every shipped code, with severity and trigger, is [docs/diagnostics/README.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/diagnostics/README.md). `DiagnosticHelpLinkTests` fails the build if a descriptor lacks its page or its index row.

## Documentation

These pages are the documentation. They also render at [orm.zeroalloc.net](https://orm.zeroalloc.net), built by [ZeroAlloc-Net/.website](https://github.com/ZeroAlloc-Net/.website).

| Cookbook recipe | Covers |
|--------|-------------|
| [flat-row.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/cookbook/flat-row.md) | Single-row reads into positional records |
| [multi-result-set.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/cookbook/multi-result-set.md) | `(head, lines)` tuple returns; `BatchMode.Auto` / `Always` / `Never` |
| [streaming.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/cookbook/streaming.md) | `IAsyncEnumerable<T>` for unbounded result sets |
| [commands.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/cookbook/commands.md) | `[Command]` — `NonQuery` / `Scalar` / `Identity` |
| [bulk-insert.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/cookbook/bulk-insert.md) | `CommandKind.BulkInsert` — chunked multi-row inserts and identity capture |
| [stored-procedures.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/cookbook/stored-procedures.md) | Output, input-output and return-value parameters; multi-result-set procedures |
| [composites.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/cookbook/composites.md) | Multi-column composites (`Money`) and `[Materialize(Factory)]` |
| [provider-quirks.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/cookbook/provider-quirks.md) | `[Dialect]` and SQLite / PostgreSQL / SQL Server / MySQL differences |
| [migrations.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/cookbook/migrations.md) | `MigrationRunner`, the three dialects, several sources in one database |
| [observability.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/cookbook/observability.md) | Composing with ZeroAlloc.Telemetry's `[Instrument]` |

- [Migrating to v2](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/migrating-to-v2.md) — parameter names without the `@` prefix, `[Param(DbType)]` on inputs, and the `IMigrationDialect` change.
- [CHANGELOG](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/CHANGELOG.md) — what shipped in each release.
- [Design](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/design/2026-05-30-v1.0-design.md) — the v1.0 architecture, generator surface and convention discovery.

## Versioning

ZeroAlloc.ORM follows SemVer. `Microsoft.CodeAnalysis.PublicApiAnalyzers` guards the public surface of `ZeroAlloc.ORM`, `ZeroAlloc.ORM.Abstractions` and `ZeroAlloc.TypeConversions`, so 2.x releases are additive and a breaking change needs a new major.

## Not supported yet

Not supported yet, roughly in order of demand: composites of composites (ZAO052 today), SQL loaded from embedded resources (`FromResource`, ZAO020 today), a MySQL migration dialect, table-valued parameters, and provider-native bulk paths such as `SqlBulkCopy` and PostgreSQL `COPY`. The working backlog is [docs/plans/za-orm-backlog.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/plans/za-orm-backlog.md).
