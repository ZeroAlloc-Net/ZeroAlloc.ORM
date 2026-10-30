using AwesomeAssertions;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.AllocationGates;

// #351 — bytes per call of one workload, written three ways; see
// SqliteWorkloadAllocationGateTests. ToleranceBytes is what a process-wide measurement may
// pick up from other threads, per measurement; zero where the count is the calling thread's.
internal sealed record WorkloadBytes(long HandWrittenAdoNet, long HandWrittenAsyncDb, long Generated, long ToleranceBytes)
{
    public long GeneratorGap => Generated - HandWrittenAsyncDb;

    public long TotalGap => Generated - HandWrittenAdoNet;

    public void ShouldFit(string workload, long generatorGapBudget, long totalGapBudget)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{workload}: hand-written ADO.NET {HandWrittenAdoNet} B, hand-written IAsyncDbConnection "
            + $"{HandWrittenAsyncDb} B, generated {Generated} B; generator gap {GeneratorGap} B, total gap {TotalGap} B");

        // A gap is the difference of two measurements, so it can pick up both tolerances.
        var slack = 2 * ToleranceBytes;
        GeneratorGap.Should().BeLessThanOrEqualTo(
            generatorGapBudget + slack, $"the generated {workload} should add no more than its budget to the substrate");
        TotalGap.Should().BeLessThanOrEqualTo(
            totalGapBudget + slack, $"the generated {workload} should cost no more than its budget over hand-written ADO.NET");
    }
}
