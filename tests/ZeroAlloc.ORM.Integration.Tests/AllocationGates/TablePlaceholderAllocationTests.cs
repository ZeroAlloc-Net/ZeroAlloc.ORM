using AwesomeAssertions;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Integration.Tests.AllocationGates;

// #464 — once warm, a method whose SQL holds {table} allocates exactly what the same method with
// the table written into the SQL does: the resolved SQL comes from a static memo or one cache
// lookup. A repository created per call allocates nothing beyond the repository object. Every call
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
    public async Task EqualStringAtAnotherAddress_AllocatesSameBytesAsLiteralMethod()
    {
        var fx = await SeededAsync().ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var literal = new TableLiteralRepo(fx.Connection);
            // The string at another address fills the memo first, then the literal-address one runs.
            var distinct = new TablePlaceholderRepo(fx.Connection, new string("Widgets".AsSpan()));
            var other = new TablePlaceholderRepo(fx.Connection, "Widgets");
            (await distinct.CountByIdAsync(1, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);

            var literalBytes = AllocationGate.MeasureBytesPerCallValueTask(
                Iterations, () => literal.CountByIdAsync(1, CancellationToken.None));
            var otherBytes = AllocationGate.MeasureBytesPerCallValueTask(
                Iterations, () => other.CountByIdAsync(1, CancellationToken.None));

            otherBytes.Should().Be(literalBytes, "an equal string at another address still hits the memo");
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

    [Fact]
    public async Task RepositoryInstance_HasNoExtraSizeOverLiteral()
    {
        var fx = await SeededAsync().ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            var conn = fx.Connection;
            // One warm call each, so the static memo is filled and cannot count towards the size.
            (await new TablePlaceholderRepo(conn, "Widgets").CountByIdAsync(1, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
            (await new TableLiteralRepo(conn).CountByIdAsync(1, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);
            (await new TableTenMethodsRepo(conn, "Widgets").CountVariant10Async(1, CancellationToken.None).ConfigureAwait(false)).Should().Be(1);

            var literalBytes = AllocationGate.MeasureBytesPerCall(
                Iterations, () => s_sink = new TableLiteralRepo(conn));
            var placeholderBytes = AllocationGate.MeasureBytesPerCall(
                Iterations, () => s_sink = new TablePlaceholderRepo(conn, "Widgets"));
            var tenBytes = AllocationGate.MeasureBytesPerCall(
                Iterations, () => s_sink = new TableTenMethodsRepo(conn, "Widgets"));
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"literal {literalBytes} B, placeholder {placeholderBytes} B, ten methods {tenBytes} B");

            (placeholderBytes - literalBytes).Should().Be(IntPtr.Size, "only the table string is stored");
            (tenBytes - literalBytes).Should().Be(IntPtr.Size, "ten placeholder methods add no per-instance field");
        }
    }

    [Fact]
    public async Task AlternatingTables_AllocateNothingBeyondLiteral()
    {
        var fx = await SeededAsync().ConfigureAwait(false);
        await using (fx.ConfigureAwait(false))
        {
            await fx.ExecuteDdlAsync("CREATE TABLE Gadgets (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL); INSERT INTO Gadgets (Id, Name) VALUES (1, 'gear')").ConfigureAwait(false);
            var widgets = new TablePlaceholderRepo(fx.Connection, "Widgets");
            var gadgets = new TablePlaceholderRepo(fx.Connection, "Gadgets");
            var literalWidgets = new TableLiteralRepo(fx.Connection);
            var literalGadgets = new TableLiteralGadgetsRepo(fx.Connection);

            var literalBytes = AllocationGate.MeasureBytesPerCallValueTask(Iterations, () =>
            {
                var first = literalWidgets.CountByIdAsync(1, CancellationToken.None);
                var second = literalGadgets.CountByIdAsync(1, CancellationToken.None);
                return new ValueTask<int>(first.Result + second.Result);
            });
            var placeholderBytes = AllocationGate.MeasureBytesPerCallValueTask(Iterations, () =>
            {
                var first = widgets.CountByIdAsync(1, CancellationToken.None);
                var second = gadgets.CountByIdAsync(1, CancellationToken.None);
                return new ValueTask<int>(first.Result + second.Result);
            });
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"literal pair {literalBytes} B/call, placeholder pair {placeholderBytes} B/call");

            placeholderBytes.Should().Be(literalBytes, "alternating tables never evict or rebuild a cached statement");
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
