using AwesomeAssertions;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Integration.Tests.AllocationGates;

// #351 — the gates of SqliteWorkloadAllocationGateTests on a real PostgreSQL server. Npgsql
// completes I/O on other threads, so AllocationGate counts bytes process-wide here, and the
// class runs in a collection of its own with nothing alongside it. A call that awaits real I/O
// allocates a different, exact amount on each path it can take, because every await that
// yields boxes a state machine. On a loaded machine a single-row read took its usual path at
// 1392 B, up to 1632 B when more awaits yielded, and 752 B when it completed synchronously, so
// windows that mixed those paths moved by over 100 B either way (#434). The three ways of a
// workload are therefore measured side by side, and each reads the bytes most of its calls
// allocate: exact, with no tolerance, so one more object per call fails the gate.
//
// The hand-written baselines bind bare names, which Npgsql resolves without a Substring, and
// read the multi-result workload as one `;`-joined command, as the generated code does on a
// PostgreSQL repository since #427.
[Collection(PostgresAllocationGateGroup.Name)]
public class PostgresWorkloadAllocationGateTests
{
    private const int Iterations = 200;

    // Unlike on Sqlite, the generator gap is not zero here: these calls complete
    // asynchronously, so each async method's state machine is boxed, and the generated
    // method's is larger than the hand-written one's. #427 measured the connection-open
    // `finally` at 41 B a call and kept it: removing it costs 120 B on a closed connection.
    // Since AdoNet.Async 1.5.6 the adapter's reader wrapper pools its state machine, so a
    // read's total gap is 128 B below 1.5.5's. Since #427 the multi-result read runs one
    // joined command instead of a batch, against joined baselines: its gaps fell from 48 B
    // and 208 B.
    private const long SingleRowReadGeneratorGap = 24;
    private const long SingleRowReadTotalGap = 104;
    private const long InsertGeneratorGap = 32;
    private const long InsertTotalGap = 80;
    private const long MultiResultGeneratorGap = 40;
    private const long MultiResultTotalGap = 120;

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
            var bytes = await MeasureAsync(
                () => new ValueTask<(OrderRow Head, List<OrderLineRow> Lines)?>(
                    HandWrittenAdoNet.GetOrderWithLinesAsync(raw, WorkloadNames.Bare, WorkloadSql.HeadId)),
                () => new ValueTask<(OrderRow Head, List<OrderLineRow> Lines)?>(
                    HandWrittenAsyncDb.GetOrderWithLinesJoinedAsync(conn, WorkloadNames.Bare, WorkloadSql.HeadId)),
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
        Func<ValueTask<TGenerated>> generated)
    {
        var (adoNet, asyncDb, generatedBytes) = await AllocationGate.MeasureBytesPerCallInterleavedAsync(
            Iterations, handWrittenAdoNet, handWrittenAsyncDb, generated).ConfigureAwait(false);
        return new(adoNet, asyncDb, generatedBytes, ToleranceBytes: 0);
    }

    private static async Task<PostgresFixture> SeededAsync()
    {
        var fx = await PostgresFixture.CreateAndInitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync(WorkloadSql.Seed).ConfigureAwait(false);
        return fx;
    }
}
