using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Data.Async;
using System.Data.Async.Adapters;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Benchmarks;

// v0.7 Phase A.3 — INSERT workload (simple non-query). Each iteration inserts
// one row and returns rows-affected (1). Hand-written / Dapper.AOT / ZA.ORM
// triad. Per-iteration ID strategy: `_nextId` increments monotonically across
// benchmark iterations. The Orders table is never truncated; PK uniqueness is
// preserved by always inserting a new value. Allocation cost of `_nextId++` is
// negligible (one increment) and consistent across all three baselines.
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class InsertBench
{
    private SqliteConnection _raw = null!;
    private IAsyncDbConnection _conn = null!;
    private InsertRepository _repo = null!;
    private int _nextId;

    [GlobalSetup]
    public async Task Setup()
    {
        _raw = new SqliteConnection("Data Source=:memory:");
        _conn = _raw.AsAsync();
        await _conn.OpenAsync().ConfigureAwait(false);

        var ddl = _conn.CreateCommand();
        await using (ddl.ConfigureAwait(false))
        {
            ddl.CommandText = "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER, Total REAL);";
            await ddl.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        _repo = new InsertRepository(_conn);
        _nextId = 0;
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _conn.DisposeAsync().ConfigureAwait(false);
        await _raw.DisposeAsync().ConfigureAwait(false);
    }

    [Benchmark(Baseline = true)]
    public async Task<int> HandWrittenAdoNet()
    {
        var id = ++_nextId;
        await using var cmd = _raw.CreateCommand();
        cmd.CommandText = "INSERT INTO Orders (Id, CustomerId, Total) VALUES (@id, @cust, @total)";
        var pId = cmd.CreateParameter(); pId.ParameterName = "@id"; pId.Value = id; cmd.Parameters.Add(pId);
        var pCust = cmd.CreateParameter(); pCust.ParameterName = "@cust"; pCust.Value = 100; cmd.Parameters.Add(pCust);
        var pTotal = cmd.CreateParameter(); pTotal.ParameterName = "@total"; pTotal.Value = 9.99m; cmd.Parameters.Add(pTotal);
        return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    [Benchmark]
    public Task<int> Dapper_AOT()
    {
        var id = ++_nextId;
        return _raw.ExecuteAsync(
            "INSERT INTO Orders (Id, CustomerId, Total) VALUES (@id, @cust, @total)",
            new { id, cust = 100, total = 9.99m });
    }

    [Benchmark]
    public Task<int> ZeroAlloc_ORM()
    {
        var id = ++_nextId;
        return _repo.InsertAsync(id, 100, 9.99m, default);
    }
}

public sealed partial class InsertRepository(IAsyncDbConnection connection)
{
    [Command("INSERT INTO Orders (Id, CustomerId, Total) VALUES (@id, @cust, @total)")]
    public partial Task<int> InsertAsync(int id, int cust, decimal total, CancellationToken ct);
}
