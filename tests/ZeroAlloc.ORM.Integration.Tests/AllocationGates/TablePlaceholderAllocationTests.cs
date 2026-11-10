using AwesomeAssertions;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Integration.Tests.AllocationGates;

// #464 — once warm, a method whose SQL holds {table} allocates exactly what the same method with
// the table written into the SQL does: the resolved SQL is a field read. A repository created per
// call pays one cache lookup and allocates nothing beyond the repository object. Every call
// completes synchronously on in-memory Sqlite, so AllocationGate counts the calling thread's bytes.
public class TablePlaceholderAllocationTests
{
    private const int Iterations = 200;

    // Where the per-call repository is stored, so the JIT cannot keep it off the heap.
    private static object? s_sink;

    [Fact]
    public async Task PlaceholderMethod_AllocatesSameBytesAsLiteralMethod()
    {
        var fx = await SeededAsync().ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var literal = new TableLiteralRepo(fx.Connection);
            var placeholder = new TablePlaceholderRepo(fx.Connection, "Widgets");

            var literalBytes = AllocationGate.MeasureBytesPerCallValueTask(
                Iterations, () => literal.CountByIdAsync(1, CancellationToken.None));
            var placeholderBytes = AllocationGate.MeasureBytesPerCallValueTask(
                Iterations, () => placeholder.CountByIdAsync(1, CancellationToken.None));
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"literal {literalBytes} B/call, placeholder {placeholderBytes} B/call");

            placeholderBytes.Should().Be(literalBytes, "the resolved SQL is read from a field once warm");
            (await placeholder.CountByIdAsync(1, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
        }
    }

    [Fact]
    public async Task FreshInstancePerCall_AddsNoAllocationOverLiteral()
    {
        var fx = await SeededAsync().ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var conn = fx.Connection;
            var literal = new TableLiteralRepo(conn);

            var literalBytes = AllocationGate.MeasureBytesPerCallValueTask(
                Iterations, () => literal.CountByIdAsync(1, CancellationToken.None));
            var repositoryBytes = AllocationGate.MeasureBytesPerCall(
                Iterations, () => s_sink = new TablePlaceholderRepo(conn, "Widgets"));
            var freshBytes = AllocationGate.MeasureBytesPerCallValueTask(
                Iterations, () =>
                {
                    var repo = new TablePlaceholderRepo(conn, "Widgets");
                    s_sink = repo;
                    return repo.CountByIdAsync(1, CancellationToken.None);
                });
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"literal {literalBytes} B/call, repository object {repositoryBytes} B, fresh instance per call {freshBytes} B/call");

            repositoryBytes.Should().BePositive();
            (freshBytes - literalBytes).Should().Be(
                repositoryBytes, "a fresh instance adds its own object and nothing else: the cached SQL is shared");
        }
    }

    private static async Task<SqliteFixture> SeededAsync()
    {
        var fx = new SqliteFixture();
        await fx.InitializeAsync().ConfigureAwait(false);
        await fx.ExecuteDdlAsync("CREATE TABLE Widgets (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL); INSERT INTO Widgets (Id, Name) VALUES (1, 'bolt')").ConfigureAwait(false);
        return fx;
    }
}
