using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Data.Sqlite;
using System.Data.Async;
using System.Data.Async.Adapters;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Benchmarks;

// v0.7 Phase A.1 — first benchmark workload. Single-row SELECT against an
// in-memory Sqlite database, measured under [MemoryDiagnoser]. Only ZA.ORM
// is wired up in this commit; hand-written ADO.NET and Dapper.AOT baselines
// land in A.2.
//
// Why Sqlite in-memory? It keeps the benchmark CI-friendly (no Docker, no
// network, no fixture warmup) and isolates the measurement to the ADO.NET
// reader + materializer hot path. The Postgres backend variant in A.4 covers
// real-provider numbers behind a `--filter "*Postgres*"` gate.
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class SingleRowReadBench
{
    private SqliteConnection _raw = null!;
    private IAsyncDbConnection _conn = null!;
    private OrderRepository _repo = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _raw = new SqliteConnection("Data Source=:memory:");
        _conn = _raw.AsAsync();
        await _conn.OpenAsync().ConfigureAwait(false);

        var cmd = _conn.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = """
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER, Total REAL);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 100, 99.95);
                """;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        _repo = new OrderRepository(_conn);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _conn.DisposeAsync().ConfigureAwait(false);
        await _raw.DisposeAsync().ConfigureAwait(false);
    }

    [Benchmark]
    public Task<OrderRow?> ZeroAlloc_ORM() => _repo.GetByIdAsync(1, default);
}

// ZA.ORM repository — generator emits the GetByIdAsync body. Uses the same
// primary-constructor connection-injection convention as the integration tests
// (the generator discovers the IAsyncDbConnection parameter automatically).
public sealed partial class OrderRepository(IAsyncDbConnection connection)
{
    [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
    public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
}

public sealed record OrderRow(int Id, int CustomerId, decimal Total);
