# AGENTS.md: ZeroAlloc.ORM

Source-generated raw-SQL data access for .NET. Consumers write the SQL in `[Query]`, `[Command]`,
`[StoredProcedure]` and `[LinqQuery]` attributes on `partial` methods; a Roslyn generator emits the
ADO.NET code against AdoNet.Async's `IAsyncDbConnection`. Five NuGet packages ship from one solution,
`ZeroAlloc.ORM.slnx`: `ZeroAlloc.ORM` (runtime and migrations), `ZeroAlloc.ORM.Abstractions`
(attributes), `ZeroAlloc.ORM.Linq` (the compile-time LINQ surface), `ZeroAlloc.ORM.Generator` and
`ZeroAlloc.TypeConversions`.

## Layout

- `ZeroAlloc.ORM.Generator` and `ZeroAlloc.TypeConversions` target `netstandard2.0`, are build-time only
  and are never AOT-published (`IsAotCompatible=false`). The generator package ships no `lib/`: its DLL
  and `ZeroAlloc.TypeConversions.dll` go under `analyzers/dotnet/cs`. In-repo users reference it with
  `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`; the `GetDependencyTargetPaths` target in
  its csproj puts TypeConversions on the analyzer path too.
- The other three packages target `net10.0;net11.0` with `IsAotCompatible=true`.
- Four projects carry `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`: Abstractions, Linq, ORM
  and TypeConversions. RS0016 and RS0017 are errors, so a public API change edits the Unshipped file
  in the same commit.
- `tests/ZeroAlloc.ORM.Generator.Tests` holds Verify snapshots, `*.verified.cs`, kept LF through
  `.gitattributes`. `*.received.*` is git-ignored: review it, then promote it to `.verified`.
- `tests/ZeroAlloc.ORM.Integration.Tests`: Sqlite in memory, PostgreSQL and SQL Server through
  Testcontainers, and the allocation gates under `AllocationGates/`.
- The three `*AotSmoke` projects are `net11.0` `PublishAot` executables run as native binaries, not tests.

## Build and test

Prerequisites: the SDK `global.json` pins (11.0.100, `rollForward: latestFeature`) and the .NET 10 and
.NET 11 runtimes, as every test project targets `net10.0;net11.0`. The PostgreSQL and SQL Server tests
need a running Docker daemon and fail without one: there is no auto-skip.

Job `build-test` in `ci.yml`:

```bash
dotnet restore
dotnet build --no-restore -c Release
dotnet test --no-build -c Release --verbosity normal --coverage --coverage-output-format cobertura --coverage-settings coverage.config.xml --results-directory TestResults
```

The job then runs ci-baseline's `coverage-gate` on `TestResults/**/*.cobertura.xml`: a line-coverage
floor that the commands above do not reproduce.

Fast inner loop, with no CI job of its own, after one `dotnet build -c Release`. Verified here:

```bash
dotnet test --project tests/ZeroAlloc.ORM.Generator.Tests -c Release -f net10.0 --no-build --filter-class "*DiagnosticHelpLinkTests"
dotnet test --project tests/ZeroAlloc.ORM.Integration.Tests -c Release -f net10.0 --no-build --filter-class "*ScalarReadTests"
```

Job `aot-publish-smoke` in `aot-smoke.yml` runs against a `postgres:18` service container:

```bash
dotnet restore tests/ZeroAlloc.ORM.AotSmoke/ZeroAlloc.ORM.AotSmoke.csproj
dotnet publish tests/ZeroAlloc.ORM.AotSmoke/ZeroAlloc.ORM.AotSmoke.csproj -c Release -r linux-x64 --self-contained true -o ./aot-smoke-out
chmod +x ./aot-smoke-out/ZeroAlloc.ORM.AotSmoke
ZAORM_AOT_PG="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres" timeout --kill-after=10s 120s ./aot-smoke-out/ZeroAlloc.ORM.AotSmoke
```

Without `ZAORM_AOT_PG` the smoke skips its PostgreSQL array path. Jobs `collision-smoke` and `telemetry-collision-smoke` in `collision-smoke.yml` do the same, without
Postgres, for `tests/ZeroAlloc.ORM.GeneratorCollision.AotSmoke` and `...TelemetryCollision.AotSmoke`.

Shared jobs with no local command: `baseline` in `baseline-checks.yml`, `api-compat` and
`release-tracking` in `ci.yml`, and `smoke` in `benchmarks.yml`, which runs every benchmark once.

Benchmarks, job `full` in `benchmarks.yml`, by hand. Postgres workloads need Docker, Sqlite ones do not:

```bash
dotnet build tests/ZeroAlloc.ORM.Benchmarks -c Release
dotnet run --project tests/ZeroAlloc.ORM.Benchmarks -c Release --no-build -- --filter "${FILTER:-*}"
```

Packing fails with `ZAVERSION001` without a version, and `ZAVERSION002` when the build did not get it.
Pass `-p:Version` to every build and to the pack. Verified:

```bash
SHA=$(git rev-parse --short HEAD)
dotnet pack src/ZeroAlloc.ORM.Generator -c Release -p:Version=9.9.9-local.$SHA -o /tmp/orm-pack
```

## Conventions and gotchas

- Do not pass `-p:PublishAot=true` on a command line. `PublishAot` is set inside the smoke csprojs, and
  a global property flows to the `netstandard2.0` generator and TypeConversions projects and fails with
  NETSDK1207.
- `Directory.Build.props` pins `AdoNetAsyncVersion` to 1.5.6 on purpose: it is the dependency floor of
  generated code, and a floating version would record whatever was latest at build time. Raise it only
  when emitted code needs a newer AdoNet.Async API.
- A new diagnostic needs four things: a descriptor in `Diagnostics/DiagnosticDescriptors.cs`, a page
  `docs/diagnostics/ZAOnnn.md`, a row in `docs/diagnostics/README.md` and an entry in
  `AnalyzerReleases.Unshipped.md`. `DiagnosticHelpLinkTests` and the build fail without them. IDs are
  never reused. `ZAO001` to `ZAO082` are attribute and SQL diagnostics, `ZAO100` to `ZAO113` LINQ ones.
- A generator change usually changes snapshots. Run the Generator tests, review each `.received.cs`
  against its `.verified.cs`, and commit only the intended `.verified.cs` changes.
- Allocation gates in `Integration.Tests/AllocationGates` hold gaps against hand-written ADO.NET, not
  absolute bytes. In the Sqlite gates the `*GeneratorGap` constants are 0: generated code must
  allocate exactly what the same code over `IAsyncDbConnection` does. A gap that shrinks
  passes; tighten its constant in the same change. The `*TotalGap` constants include AdoNet.Async's own
  cost, so a reviewer must not read them as the generator's.
- The Benchmarks project targets only `net10.0` and the AOT smokes only `net11.0`, unlike the org
  default of every LTS framework. That is deliberate here. Keep `[module: DapperAot]` in the
  Benchmarks `Program.cs`: without it the Dapper.AOT rows fall back to reflection Dapper.

<!-- org-core:start sha256=8faf417fa86f11bd8ad9bc98dfa4683fba44e440291e84c6cb9b0ec3ca0c0e7e (generated from docker-nuc scripts/agents-md/org-core-zeroalloc.md; do not edit here) -->
## Org rules

These rules hold in every ZeroAlloc-Net repo. Each library makes two promises: it works under
NativeAOT and trimming, and its hot path allocates nothing. The 2026-10-11 sweep found 16 of 28 repos
allocating on the hot path, most with docs claiming 0 B.

### Hot path

The operation a consumer runs repeatedly: `Send`, a cache hit, `Validate`, `Map`, `Serialize`, a
resilience call, a state transition, an append, a worker poll, including the idle poll. Startup and
registration are not hot path. For a source generator, the hot path is the code it emits: judge the
snapshots and the generated output.

### Allocation traps the sweep found

| Pattern | Instead |
|---|---|
| `async ValueTask` that only awaits and returns | `return new ValueTask<T>(inner.XAsync(ct));` when the method only forwards a `Task<T>` (return a forwarded `ValueTask<T>` directly). The wrapper stores a synchronous throw in the returned task; the direct return lets it, and the cancellation status of the callee, reach the caller. Keep the `async` wrapper when callers depend on either |
| `Task.WhenAll` over tasks that already completed | skip the await only when every task has `IsCompletedSuccessfully` (`Status == TaskStatus.RanToCompletion` on `netstandard2.0`); otherwise `await Task.WhenAll`, which also reports faults of completed tasks |
| `TagList` or log `params object[]` built before `Enabled`/`IsEnabled` | check first, or `LoggerMessage.Define` |
| event args allocated before checking for subscribers | copy the handler to a local, check, then allocate |
| `new Serializer()` / `new Utf8JsonWriter()` per call | cache stateless instances, reuse writers with `Reset` |
| `GetString()` / `ToString()` round-trips in JSON converters | parse and format through spans |
| `ToArray()` / `ToList()` copies between layers | pass `ReadOnlyMemory<T>` or spans, copy once at most |
| LINQ in emitted code | emit `for` loops |
| `yield` iterator behind a public `GetEnumerator` | public struct enumerator, from the first release |
| `Enum.IsDefined(typeof(E), v)` with a boxed value, `decimal.GetBits(v)` to read the scale | `Enum.IsDefined<E>(v)` when `v` has type `E` (.NET 5 or later, so not on .NET Framework, `netstandard2.0`, `netstandard2.1` or .NET Core 3.1); `v.Scale` for a scale read only (.NET 7 or later, never for the coefficient or sign). Keep the old call on older targets |
| configuration parsed or read per call | parse once and cache |
| an `AsyncLocal` write per call | pass state explicitly |
| a capturing lambda or method-group delegate per call | cache the delegate in a field, or a static lambda |
| a per-key object that is never removed | remove on release; otherwise it is a leak |

Inherent allocations are fine when documented: the consumer's own result, an exception on a failure
path, a one-time cache fill.

### Tests and benchmarks

- Every hot path has an allocation gate test (`AllocationGate` from ZeroAlloc.TestHelpers) that measures
  the whole public call, including any state setup.
- Budgets are exact. If a path allocates, assert that number and comment what it is.
- Test the generated code, not a hand-written stand-in, and cover every shape: each attribute, nested
  collections, and the asynchronous case with a fake that returns pending tasks.
- Benchmarks run in CI through the shared `benchmark-smoke.yml` and target the library's LTS
  frameworks. The benchmark body allocates nothing besides the library itself. Run locally with
  `--inProcess`.
- Publish exact allocations and same-run ratios against a `Baseline`, never absolute timings. Run with
  `--launchCount 3`, pin CPUs with `taskset`, give the spread, and label every table.

### NativeAOT and trimming

- Packable runtime projects, and projects consumers publish, set `IsAotCompatible=true` on modern
  target frameworks. Analyzers and source generators target `netstandard2.0`.
- No reflection or runtime code generation on a supported path. An opt-in reflection fallback carries
  honest `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Every suppression must be true;
  remove dead ones.
- The AOT smoke sample publishes with `TreatWarningsAsErrors=true`. If a third-party warning really
  blocks it, use a narrow, justified suppression for that warning. `WarningsNotAsErrors` selects codes,
  not assemblies, so it would also downgrade the same warning from generated code.
- The smoke runs the hot path of every package in the repo. A package without a smoke has unknown AOT
  status. Adapters run against real services in CI service containers; publish-only is a last resort.
  App and template smokes make authenticated requests.

### Docs and shipping

- Docs match measurements. After a hot-path change, re-measure and update `docs/performance.md` and the
  README. Never write "0 B" without a test that asserts it.
- New public API is `feat:`, even for a performance change. Public API changes are additive: never add
  an optional parameter to a shipped signature. A breaking end state goes in a `next-major` issue.
- A behaviour change gets a `## Behaviour change` section in the PR body.
- Conventional Commits with release-please. Subjects start lowercase, body lines stay within 100
  characters, and a body never contains nested parentheses: release-please drops the whole commit.
- Local packs use `9.9.9-local.<sha>`. After a release, verify the package on NuGet.
- Fix review findings in the PR; file an issue only for what cannot go in it.

Full rules, template wiring lessons and the reasons behind each rule: https://github.com/ZeroAlloc-Net/.github/blob/main/AGENTS.md
<!-- org-core:end -->
