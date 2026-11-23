using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.AllocationGates;

// Every allocation gate in this project runs in this collection, which xunit runs after the
// parallel collections and with nothing alongside it.
//   * #351 — PostgresWorkloadAllocationGateTests counts bytes process-wide, so another test's
//     allocations would land in its windows.
//   * #493 — AllocationGate waits for tiered compilation to settle before it measures, and gives
//     up after 20 s. Run in parallel with the Testcontainers tests, whose SQL Server and Postgres
//     containers start and the provider code they JIT, the runtime kept tiering busy past that
//     limit on CI, and the Sqlite gates failed without comparing a byte.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationGateGroup
{
    public const string Name = "Allocation gates";
}
