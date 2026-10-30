using AwesomeAssertions;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Integration.Tests.AllocationGates;

// #351 — the gates of SqliteWorkloadAllocationGateTests on a real PostgreSQL server. Npgsql
// completes I/O on other threads, so AllocationGate counts bytes process-wide here, and the
// class runs in a collection of its own with nothing alongside it. Each measurement may then
// pick up DefaultAsyncToleranceBytesPerCall of another thread's bytes, so a gap may read up to
// twice that above its budget: still below the 24 B of the smallest object, so one more
// allocation per call fails the gate.
//
// The hand-written baselines bind bare names, which Npgsql resolves without a Substring, and
// read the multi-result workload through a batch, as the generated code does where the
// provider supports one.
[Collection(PostgresAllocationGateGroup.Name)]
public class PostgresWorkloadAllocationGateTests
{
    private const int Iterations = 200;

    // Unlike on Sqlite, the generator gap is not zero here: these calls complete
    // asynchronously, so each async method's state machine is boxed, and the generated
    // method's is larger than the hand-written one's. The state-machine boxes and the
    // connection-open `finally` are #427. Since AdoNet.Async 1.5.6 the adapter's reader
    // wrapper pools its state machine, so a read's total gap is 128 B below 1.5.5's.
    private const long SingleRowReadGeneratorGap = 24;
    private const long SingleRowReadTotalGap = 104;
    private const long InsertGeneratorGap = 32;
    private const long InsertTotalGap = 80;
    private const long MultiResultGeneratorGap = 48;
    private const long MultiResultTotalGap = 208;

    private int _nextId;

    [Fact]
    public async Task Single_row_read_stays_within_its_budget()
    {
        var fx = await SeededAsync().ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var repo = new PostgresWorkloadRepo(fx.Connection);
            var raw = fx.RawConnection;
            var conn = fx.Connection;

            var bytes = await MeasureAsync(
                () => new ValueTask<OrderRow?>(HandWrittenAdoNet.GetByIdAsync(raw, WorkloadNames.Bare, WorkloadSql.HeadId)),
                () => new ValueTask<OrderRow?>(HandWrittenAsyncDb.GetByIdAsync(conn, WorkloadNames.Bare, WorkloadSql.HeadId)),
                () => new ValueTask<OrderRow?>(repo.GetByIdAsync(WorkloadSql.HeadId, CancellationToken.None))).ConfigureAwait(false);
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
            var repo = new PostgresWorkloadRepo(fx.Connection);
            var raw = fx.RawConnection;
            var conn = fx.Connection;

            var bytes = await MeasureAsync(
                () => new ValueTask<int>(HandWrittenAdoNet.InsertAsync(raw, WorkloadNames.Bare, ++_nextId, 100, 9.99m)),
                () => new ValueTask<int>(HandWrittenAsyncDb.InsertAsync(conn, WorkloadNames.Bare, ++_nextId, 100, 9.99m)),
                () => new ValueTask<int>(repo.InsertAsync(++_nextId, 100, 9.99m, CancellationToken.None))).ConfigureAwait(false);
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
            var repo = new PostgresWorkloadRepo(fx.Connection);
            var raw = fx.RawConnection;
            var conn = fx.Connection;
            conn.CanCreateBatch.Should().BeTrue("the generated method and both baselines read through a batch");

            var bytes = await MeasureAsync(
                () => new ValueTask<(OrderRow Head, List<OrderLineRow> Lines)?>(
                    HandWrittenAdoNet.GetOrderWithLinesBatchAsync(raw, WorkloadNames.Bare, WorkloadSql.HeadId)),
                () => new ValueTask<(OrderRow Head, List<OrderLineRow> Lines)?>(
                    HandWrittenAsyncDb.GetOrderWithLinesAsync(conn, WorkloadNames.Bare, WorkloadSql.HeadId)),
                () => new ValueTask<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?>(
                    repo.GetOrderWithLinesAsync(WorkloadSql.HeadId, CancellationToken.None))).ConfigureAwait(false);
            bytes.ShouldFit("multi-result read", MultiResultGeneratorGap, MultiResultTotalGap);

            var result = await repo.GetOrderWithLinesAsync(WorkloadSql.HeadId, CancellationToken.None).ConfigureAwait(false);
            result.Should().NotBeNull();
            result!.Value.Lines.Should().HaveCount(WorkloadSql.LineCount);
        }
    }

    private static async Task<WorkloadBytes> MeasureAsync<TAdoNet, TAsyncDb, TGenerated>(
        Func<ValueTask<TAdoNet>> handWrittenAdoNet,
        Func<ValueTask<TAsyncDb>> handWrittenAsyncDb,
        Func<ValueTask<TGenerated>> generated) =>
        new(
            await AllocationGate.MeasureBytesPerCallAsync(Iterations, handWrittenAdoNet).ConfigureAwait(false),
            await AllocationGate.MeasureBytesPerCallAsync(Iterations, handWrittenAsyncDb).ConfigureAwait(false),
            await AllocationGate.MeasureBytesPerCallAsync(Iterations, generated).ConfigureAwait(false),
            AllocationGate.DefaultAsyncToleranceBytesPerCall);

    private static async Task<PostgresFixture> SeededAsync()
    {
        var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(WorkloadSql.Seed).ConfigureAwait(false);
        return fx;
    }
}
