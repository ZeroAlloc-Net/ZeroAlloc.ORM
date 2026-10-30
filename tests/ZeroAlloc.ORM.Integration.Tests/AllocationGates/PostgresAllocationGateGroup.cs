using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.AllocationGates;

// #351 — PostgresWorkloadAllocationGateTests counts bytes process-wide, so it runs with no
// other test alongside it.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresAllocationGateGroup
{
    public const string Name = "Postgres allocation gates";
}
