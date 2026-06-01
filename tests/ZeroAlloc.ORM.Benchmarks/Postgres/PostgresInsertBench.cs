using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Dapper;
using Npgsql;
using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Benchmarks.Postgres;

// v0.7 Phase A.4 — Postgres variant of InsertBench. Same single-row INSERT
// returning rows-affected. Gated behind the "Postgres" category.
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[BenchmarkCategory("Postgres")]
public class PostgresInsertBench
{
    private PostgresBenchFixture _fx = null!;
    private NpgsqlConnection _raw = null!;
    private PostgresInsertRepository _repo = null!;
    private int _nextId;

    [GlobalSetup]
    public async Task Setup()
    {
        _fx = new PostgresBenchFixture();
        await _fx.InitializeAsync().ConfigureAwait(false);
        _raw = _fx.RawConnection;
        await _fx.ExecuteDdlAsync(
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER, Total NUMERIC(18,4));")
            .ConfigureAwait(false);
        _repo = new PostgresInsertRepository(_fx.Connection);
        _nextId = 0;
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _fx.DisposeAsync().ConfigureAwait(false);

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

public sealed partial class PostgresInsertRepository(IAsyncDbConnection connection)
{
    [Command("INSERT INTO Orders (Id, CustomerId, Total) VALUES (@id, @cust, @total)")]
    public partial Task<int> InsertAsync(int id, int cust, decimal total, CancellationToken ct);
}
