using AwesomeAssertions;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Integration.Tests.AllocationGates;

// #351 — allocation budgets for the benchmark suite's single-statement workloads on
// in-memory Sqlite, against hand-written code doing the same work. Every call completes
// synchronously, so AllocationGate counts the calling thread's bytes exactly and throws
// if a call does not.
//
// Each gate measures three methods doing the same work: hand-written over the provider's
// SqliteConnection, hand-written over IAsyncDbConnection, and the generated one. It holds
// two gaps to the bytes measured once parameter names were spelled per dialect:
//   * Generator gap: generated minus hand-written over IAsyncDbConnection. What the
//     generated code adds on top of the substrate it calls.
//   * Total gap: generated minus hand-written over SqliteConnection. The generator gap
//     plus AdoNet.Async's own cost, which MarcelRoozekrans/AdoNet.Async#365 works on.
// A gap that shrinks passes; tighten its budget then. A gap that grows fails.
public class SqliteWorkloadAllocationGateTests
{
    private const int Iterations = 200;

    // Measured at #351. The generated methods allocate exactly what the same code over
    // IAsyncDbConnection does; the rest is AdoNet.Async's.
    private const long SingleRowReadGeneratorGap = 0;
    private const long SingleRowReadTotalGap = 72;
    private const long InsertGeneratorGap = 0;
    private const long InsertTotalGap = 48;
    private const long MultiResultGeneratorGap = 0;
    private const long MultiResultTotalGap = 72;

    // The insert gate's ids, one per call; ids only grow, so no insert collides.
    private int _nextId;

    [Fact]
    public async Task Single_row_read_stays_within_its_budget()
    {
        var fx = await SeededAsync().ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var repo = new SqliteWorkloadRepo(fx.Connection);
            var raw = fx.RawConnection;
            var conn = fx.Connection;

            var bytes = Measure(
                () => new ValueTask<OrderRow?>(HandWrittenAdoNet.GetByIdAsync(raw, WorkloadNames.Prefixed, WorkloadSql.HeadId)),
                () => new ValueTask<OrderRow?>(HandWrittenAsyncDb.GetByIdAsync(conn, WorkloadNames.Prefixed, WorkloadSql.HeadId)),
                () => new ValueTask<OrderRow?>(repo.GetByIdAsync(WorkloadSql.HeadId, CancellationToken.None)));
            bytes.ShouldFit("single-row read", SingleRowReadGeneratorGap, SingleRowReadTotalGap);

            (await repo.GetByIdAsync(WorkloadSql.HeadId, CancellationToken.None).ConfigureAwait(false))
                .Should().Be(new OrderRow(WorkloadSql.HeadId, 100, 99.95m));
        }
    }

    [Fact]
    public async Task Insert_stays_within_its_budget()
    {
        var fx = await SeededAsync().ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var repo = new SqliteWorkloadRepo(fx.Connection);
            var raw = fx.RawConnection;
            var conn = fx.Connection;

            var bytes = Measure(
                () => new ValueTask<int>(HandWrittenAdoNet.InsertAsync(raw, WorkloadNames.Prefixed, ++_nextId, 100, 9.99m)),
                () => new ValueTask<int>(HandWrittenAsyncDb.InsertAsync(conn, WorkloadNames.Prefixed, ++_nextId, 100, 9.99m)),
                () => new ValueTask<int>(repo.InsertAsync(++_nextId, 100, 9.99m, CancellationToken.None)));
            bytes.ShouldFit("insert", InsertGeneratorGap, InsertTotalGap);

            (await repo.InsertAsync(++_nextId, 100, 9.99m, CancellationToken.None).ConfigureAwait(false))
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task Multi_result_read_stays_within_its_budget()
    {
        var fx = await SeededAsync().ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var repo = new SqliteWorkloadRepo(fx.Connection);
            var raw = fx.RawConnection;
            var conn = fx.Connection;

            var bytes = Measure(
                () => new ValueTask<(OrderRow Head, List<OrderLineRow> Lines)?>(HandWrittenAdoNet.GetOrderWithLinesAsync(raw, WorkloadNames.Prefixed, WorkloadSql.HeadId)),
                () => new ValueTask<(OrderRow Head, List<OrderLineRow> Lines)?>(HandWrittenAsyncDb.GetOrderWithLinesAsync(conn, WorkloadNames.Prefixed, WorkloadSql.HeadId)),
                () => new ValueTask<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?>(repo.GetOrderWithLinesAsync(WorkloadSql.HeadId, CancellationToken.None)));
            bytes.ShouldFit("multi-result read", MultiResultGeneratorGap, MultiResultTotalGap);

            var result = await repo.GetOrderWithLinesAsync(WorkloadSql.HeadId, CancellationToken.None).ConfigureAwait(false);
            result.Should().NotBeNull();
            result!.Value.Lines.Should().HaveCount(WorkloadSql.LineCount);
        }
    }

    private static WorkloadBytes Measure<TAdoNet, TAsyncDb, TGenerated>(
        Func<ValueTask<TAdoNet>> handWrittenAdoNet,
        Func<ValueTask<TAsyncDb>> handWrittenAsyncDb,
        Func<ValueTask<TGenerated>> generated) =>
        new(
            AllocationGate.MeasureBytesPerCallValueTask(Iterations, handWrittenAdoNet),
            AllocationGate.MeasureBytesPerCallValueTask(Iterations, handWrittenAsyncDb),
            AllocationGate.MeasureBytesPerCallValueTask(Iterations, generated),
            ToleranceBytes: 0);

    private static async Task<SqliteFixture> SeededAsync()
    {
        var fx = new SqliteFixture();
        await fx.InitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(WorkloadSql.Seed).ConfigureAwait(false);
        return fx;
    }
}
