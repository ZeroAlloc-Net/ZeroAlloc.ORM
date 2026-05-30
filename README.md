<h1 align="center">ZeroAlloc.ORM</h1>

<p align="center">Source-generator-based, NativeAOT-clean raw-SQL data access for .NET. Annotate <code>partial</code> methods with <code>[Query]</code> / <code>[Command]</code> / <code>[StoredProcedure]</code>; the generator emits typed parameter binding + materialization against <a href="https://github.com/MarcelRoozekrans/AdoNet.Async">AdoNet.Async</a>. Zero runtime reflection.</p>

> **Status:** Pre-release. v0.1 milestone in progress. Authoritative design lives at [`docs/design/2026-05-30-v1.0-design.md`](docs/design/2026-05-30-v1.0-design.md). Working backlog at [`docs/plans/za-orm-backlog.md`](docs/plans/za-orm-backlog.md).

## What it is

A source-generator-driven data-access library that fills in the gap between two extremes adopters currently choose from:

- **EF Core** — full LINQ-to-SQL ORM, but its precompile-queries pipeline currently collides with co-resident source generators (e.g. ZA.Rest), blocking NativeAOT publish in template stacks like [ZeroAlloc.Templates](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates).
- **Hand-written ADO.NET** — works under AOT, but every repository becomes a hand-shaped tower of `CreateCommand` / `CreateParameter` / `ReadAsync` calls.

ZeroAlloc.ORM is the middle path: write the SQL string in an attribute, declare the partial method signature, let the source generator emit the ADO.NET pipeline. Zero runtime reflection, fully AOT-publishable, idiomatic with the rest of the ZeroAlloc ecosystem (consumes `AdoNet.Async`, dogfoods `ZeroAlloc.ValueObjects`, shares the convention catalog with `ZeroAlloc.Mapping`).

## Packages

| Package | Description | NativeAOT |
|---------|-------------|---|
| **ZeroAlloc.ORM** | Runtime helpers + `ActivitySource` for observability. Depends on AdoNet.Async. | ✅ |
| **ZeroAlloc.ORM.Abstractions** | Public attribute surface (`[Query]`, `[Param]`) + exception types. Other attributes (`[Command]`, `[StoredProcedure]`, `[Materialize]`, `[StoreAsString]`) land in their implementing milestones (v0.2–v0.5). | ✅ |
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

Deferred to later milestones: `[Command]` / `[StoredProcedure]` (v0.4), `IAsyncEnumerable<T>` streaming (v0.3), multi-result-set tuples (v0.3), value-objects + enums (v0.2), composite types (v0.5).

## Design + roadmap

- **Design:** [`docs/design/2026-05-30-v1.0-design.md`](docs/design/2026-05-30-v1.0-design.md) — 5-section v1.0 design covering architecture, generator surface, convention discovery, diagnostics, test strategy, milestones.
- **Backlog:** [`docs/plans/za-orm-backlog.md`](docs/plans/za-orm-backlog.md) — priority-banded task list. New findings during implementation get appended.
- **Ecosystem context:** sits alongside [ZeroAlloc.Mediator](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator), [ZeroAlloc.Mapping](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping), [ZeroAlloc.ValueObjects](https://github.com/ZeroAlloc-Net/ZeroAlloc.ValueObjects), [ZeroAlloc.Validation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation). Substrate is [AdoNet.Async](https://github.com/MarcelRoozekrans/AdoNet.Async) (AOT-compatible since v1.x).

## License

[MIT](LICENSE)
