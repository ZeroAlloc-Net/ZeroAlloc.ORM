using AwesomeAssertions;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Integration.Tests;

// #331 - a ValueTask<T> query that completes synchronously, as every in-memory
// Sqlite query does, allocates exactly one Task<int> less than the same query
// declared as Task<T>. Everything else either call allocates is the provider's,
// and the two calls make the same provider calls.
public class ValueTaskQueryAllocationTests
{
    private const int Iterations = 200;

    [Fact]
    public async Task ValueTask_query_saves_exactly_the_Task_allocation()
    {
        var fx = new SqliteFixture();
        await using (fx.ConfigureAwait(false))
        {
            await fx.InitializeAsync().ConfigureAwait(false);
            var repo = new ValueTaskQueryRepo(fx.Connection);

            // AllocationGate throws if any of these does not complete synchronously.
            var taskBytes = AllocationGate.MeasureBytesPerCallValueTask(
                Iterations, () => new ValueTask<int>(repo.AnswerAsTaskAsync(CancellationToken.None)));
            var valueTaskBytes = AllocationGate.MeasureBytesPerCallValueTask(
                Iterations, () => repo.AnswerAsync(CancellationToken.None));
            var oneTaskBytes = AllocationGate.MeasureBytesPerCallValueTask(
                Iterations, () => new ValueTask<int>(CompletedAsync()));
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Task<int> query {taskBytes} B/call, ValueTask<int> query {valueTaskBytes} B/call, "
                + $"one Task<int> {oneTaskBytes} B");

            oneTaskBytes.Should().BePositive();
            (taskBytes - valueTaskBytes).Should().Be(
                oneTaskBytes, "the ValueTask<T> query should save the Task<int> and nothing else");

            (await repo.AnswerAsync(CancellationToken.None).ConfigureAwait(false)).Should().Be(42);
        }
    }

    // An async method that completes synchronously with a result the runtime does
    // not cache: what the Task<int> query allocates for its Task.
    private static async Task<int> CompletedAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return 42;
    }
}
