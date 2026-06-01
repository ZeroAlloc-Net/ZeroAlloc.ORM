# ZeroAlloc.ORM benchmarks

This directory holds reproducible BenchmarkDotNet numbers comparing
**hand-written ADO.NET**, **Dapper.AOT**, and **ZeroAlloc.ORM** across four
canonical workloads on two backends.

The benchmark project lives at
[`tests/ZeroAlloc.ORM.Benchmarks`](../../tests/ZeroAlloc.ORM.Benchmarks).

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

The benchmarks require **.NET SDK 10.0.300** (see [`global.json`](../../global.json)).
BenchmarkDotNet only produces meaningful numbers in `Release` configuration.

### Default — Sqlite only (CI-safe, no Docker required)

```bash
dotnet run -c Release --project tests/ZeroAlloc.ORM.Benchmarks -- \
  --filter "*Sqlite*" --exporters json markdown
```

`BenchmarkSwitcher` matches `--filter` against the full benchmark name —
`*Sqlite*` won't match Sqlite benchmarks directly because the class names
don't contain "Sqlite". In practice use one of:

```bash
# Run a specific Sqlite workload
dotnet run -c Release --project tests/ZeroAlloc.ORM.Benchmarks -- \
  --filter "*SingleRowReadBench*"

# Run every non-Postgres benchmark
dotnet run -c Release --project tests/ZeroAlloc.ORM.Benchmarks -- \
  --filter "ZeroAlloc.ORM.Benchmarks.SingleRowReadBench*" \
  --filter "ZeroAlloc.ORM.Benchmarks.MultiRowReadBench*" \
  --filter "ZeroAlloc.ORM.Benchmarks.MultiResultSetBench*" \
  --filter "ZeroAlloc.ORM.Benchmarks.InsertBench*"
```

### Postgres opt-in (requires Docker)

```bash
dotnet run -c Release --project tests/ZeroAlloc.ORM.Benchmarks -- \
  --filter "*Postgres*"
```

Or include the category explicitly:

```bash
dotnet run -c Release --project tests/ZeroAlloc.ORM.Benchmarks -- \
  --anyCategories Postgres
```

Testcontainers boots `postgres:16-alpine` per fixture, so Docker Desktop /
Docker Engine must be reachable. Postgres workloads are markedly slower than
Sqlite — expect ~10× wall-clock for the full suite.

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
| `v0.7.0-sqlite-results.md`      | Sqlite   | (pending)   | First CI/local run with SDK 10.0.300 |
| `v0.7.0-postgres-results.md`    | Postgres | (pending)   | Local-only; Docker required        |

Until an environment with SDK 10.0.300 is available locally, results files
hold the reproduction recipe rather than captured numbers — adopters and
contributors should re-run on their own hardware. ZA.ORM is a relative-
position tool; absolute numbers shift between CPUs but the ratios are
stable.
