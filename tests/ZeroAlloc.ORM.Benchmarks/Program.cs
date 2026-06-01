using BenchmarkDotNet.Running;
using Dapper;

// Dapper.AOT relies on this module-level attribute to intercept Dapper call-sites
// (e.g. QueryFirstOrDefaultAsync, QueryAsync) at build time. Keep here — NOT inside
// a per-benchmark file — so it survives file refactors. Removing the attribute
// silently drops every Dapper_AOT benchmark back to reflection-based Dapper,
// defeating the comparison.
[module: DapperAot]

// Mirrors ZA.Rest's BDN runner — `BenchmarkSwitcher.FromAssembly(...).Run(args)`
// honours `--filter`, `--list`, `--exporters`, `--iterationCount`, etc. straight
// from the command line. Postgres workloads are tagged
// `[BenchmarkCategory("Postgres")]`; opt in with `--anyCategories Postgres`
// (see docs/benchmarks/README.md).
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

namespace ZeroAlloc.ORM.Benchmarks
{
    // Placeholder type so the assembly has a stable anchor for FromAssembly().
    internal static class Program;
}
