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
dotnet run --project tests/ZeroAlloc.ORM.Benchmarks -c Release -- \
  --filter "ZeroAlloc.ORM.Benchmarks.SingleRowReadBench*" \
           "ZeroAlloc.ORM.Benchmarks.MultiRowReadBench*" \
           "ZeroAlloc.ORM.Benchmarks.MultiResultSetBench*" \
           "ZeroAlloc.ORM.Benchmarks.InsertBench*" \
  --job Default --launchCount 3
```

The filter selects the four Sqlite classes. Without a filter or a category,
`BenchmarkSwitcher` asks interactively which benchmarks to run.

### Postgres (slower, requires Docker)

```bash
dotnet run --project tests/ZeroAlloc.ORM.Benchmarks -c Release -- --anyCategories Postgres --job Default --launchCount 3
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
quoting these. Run with `--launchCount 3` so each ratio averages over several processes, and pin
the process to fixed CPUs where you can, for example `taskset -c 2-5 dotnet run ...`.

## Captured results

| File                            | Backend  | Captured on | Notes                              |
|---------------------------------|----------|-------------|------------------------------------|
| `v2.x-sqlite-results.md`        | Sqlite   | 2026-10-21  | 2.4.0; idle Windows 11 laptop, SDK 10.0.402, BenchmarkDotNet 0.15.8, 3 launches, pinned to 2 cores |
| `v2.x-postgres-results.md`      | Postgres | 2026-10-21  | 2.4.0; same setup, Docker `postgres:16-alpine` on the same machine |
| `v0.7.0-sqlite-results.md`      | Sqlite   | 2026-06-01  | Historical: v0.7.0, Windows 11, SDK 10.0.300, BenchmarkDotNet 0.15.2 |

The 2.x captures publish exact allocations and same-run ratios only; absolute timings are not
published, because they drift between runs and machines while a ratio measured side by side holds. The v0.7.0 file keeps its absolute timings
from a different machine; do not compare them with the 2.x ratios.
