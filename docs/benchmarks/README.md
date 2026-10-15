# ZeroAlloc.ORM benchmarks

This directory holds reproducible BenchmarkDotNet numbers comparing
**hand-written ADO.NET**, **Dapper.AOT**, and **ZeroAlloc.ORM** across four
canonical workloads on two backends.

The benchmark project lives at
[`tests/ZeroAlloc.ORM.Benchmarks`](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/tree/main/tests/ZeroAlloc.ORM.Benchmarks).

## Workloads

| File                                              | Workload                              | Rows | Backends     |
|---------------------------------------------------|---------------------------------------|------|--------------|
| `SingleRowReadBench.cs`                           | `SELECT … WHERE Id = @id`             | 1    | Sqlite, PG\* |
| `MultiRowReadBench.cs`                            | `SELECT … ORDER BY Id` → `List<T>`    | 1000 | Sqlite, PG\* |
| `MultiResultSetBench.cs`                          | `SELECT head; SELECT lines` (tuple)   | 1+10 | Sqlite, PG\* |
| `InsertBench.cs`                                  | `INSERT … VALUES (…)` → rows-affected | 1    | Sqlite, PG\* |

\* `PG` = the parallel files under `Postgres/`, gated behind the
`[BenchmarkCategory("Postgres")]` attribute.

Each class declares three `[Benchmark]` methods that all return identical
shapes — divergence between them is pure framework overhead.

## How to run

The benchmarks use the .NET SDK pinned in [`global.json`](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/global.json), currently 10.0.401 with `rollForward: latestFeature`.
BenchmarkDotNet only produces meaningful numbers in `Release` configuration.

### Sqlite (default, fast, CI-safe, no Docker required)

```bash
dotnet run --project tests/ZeroAlloc.ORM.Benchmarks -c Release
```

This runs every benchmark **not** marked `[BenchmarkCategory("Postgres")]` —
i.e. the Sqlite suite. The Postgres classes are tagged with that category and
opted out by default so a plain `dotnet run` does not require Docker.

### Postgres (slower, requires Docker)

```bash
dotnet run --project tests/ZeroAlloc.ORM.Benchmarks -c Release -- --anyCategories Postgres
```

Postgres benchmarks boot a Testcontainers `postgres:16-alpine` container per
benchmark class. Slower setup, real-provider numbers. Budget ~10× wall-clock
for the full Postgres suite versus Sqlite.

## Interpreting the output

BenchmarkDotNet prints a summary table sorted **fastest → slowest** (the
`[Orderer(SummaryOrderPolicy.FastestToSlowest)]` attribute). The
`HandWrittenAdoNet` method is marked `Baseline = true` — every other row's
**Ratio** column reports the multiple of the baseline's mean time.

Three columns matter most:

| Column     | Meaning                                                          |
|------------|------------------------------------------------------------------|
| `Mean`     | Average wall-clock per invocation                                |
| `Allocated`| Bytes allocated per invocation (`[MemoryDiagnoser]`)              |
| `Ratio`    | Mean ÷ baseline mean — quick "how much overhead does X add?"     |

Numbers vary by machine. Trust **ratios**, not absolute throughput, when
quoting these.

## Captured results

| File                            | Backend  | Captured on | Notes                              |
|---------------------------------|----------|-------------|------------------------------------|
| `v0.7.0-sqlite-results.md`      | Sqlite   | 2026-06-01  | Windows 11, SDK 10.0.300, BenchmarkDotNet 0.15.2 |
| `v0.7.0-postgres-results.md`    | Postgres | (pending)   | Local-only; Docker required; holds the reproduction recipe |

Both files date from v0.7.0. Re-capturing on 2.x, and capturing Postgres for
the first time, is tracked in
[#344](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/issues/344). ZA.ORM is
a relative-position tool; absolute numbers shift between CPUs but the ratios
are stable.
