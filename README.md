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
| **ZeroAlloc.ORM.Abstractions** | Public attribute surface (`[Query]`, `[Command]`, `[StoredProcedure]`, `[Param]`, `[Materialize]`, `[StoreAsString]`), exception types. | ✅ |
| **ZeroAlloc.ORM.Generator** | Roslyn incremental source generator. Build-time only. | N/A |
| **ZeroAlloc.TypeConversions** | Shared convention-discovery catalog (value-objects, enums, composites). Build-time only. | N/A |
| **ZeroAlloc.ORM.Analyzers** | Compile-time diagnostics (ZAO001-ZAO070). Build-time only. | N/A |

## Quick Start

_Not yet — v0.1 milestone is in progress. Once the generator emits its first partial method, the canonical Quick Start lands here._

## Design + roadmap

- **Design:** [`docs/design/2026-05-30-v1.0-design.md`](docs/design/2026-05-30-v1.0-design.md) — 5-section v1.0 design covering architecture, generator surface, convention discovery, diagnostics, test strategy, milestones.
- **Backlog:** [`docs/plans/za-orm-backlog.md`](docs/plans/za-orm-backlog.md) — priority-banded task list. New findings during implementation get appended.
- **Ecosystem context:** sits alongside [ZeroAlloc.Mediator](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator), [ZeroAlloc.Mapping](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mapping), [ZeroAlloc.ValueObjects](https://github.com/ZeroAlloc-Net/ZeroAlloc.ValueObjects), [ZeroAlloc.Validation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation). Substrate is [AdoNet.Async](https://github.com/MarcelRoozekrans/AdoNet.Async) (AOT-compatible since v1.x).

## License

[MIT](LICENSE)
