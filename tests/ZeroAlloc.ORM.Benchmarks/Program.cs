using BenchmarkDotNet.Running;

// Mirrors ZA.Rest's BDN runner — `BenchmarkSwitcher.FromAssembly(...).Run(args)`
// honours `--filter`, `--list`, `--exporters`, `--iterationCount`, etc. straight
// from the command line. The Postgres backend benchmarks live behind a category
// filter (`--filter "*Postgres*"`) so the default invocation runs Sqlite only.
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

namespace ZeroAlloc.ORM.Benchmarks
{
    // Placeholder type so the assembly has a stable anchor for FromAssembly().
    internal static class Program;
}
