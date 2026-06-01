using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Dapper;
using Npgsql;
using System.Data.Async;
using System.Runtime.CompilerServices;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Benchmarks.Postgres;

// v0.7 Phase A.4 — Postgres variant of MultiRowReadBench. Same 1000-row
// payload, NUMERIC(18,4) backing column (provider-native decimal). Gated
// behind the "Postgres" category.
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[BenchmarkCategory("Postgres")]
public class PostgresMultiRowReadBench
{
    private const int RowCount = 1000;

    private PostgresBenchFixture _fx = null!;
    private NpgsqlConnection _raw = null!;
    private PostgresMultiRowRepository _repo = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _fx = new PostgresBenchFixture();
        await _fx.InitializeAsync().ConfigureAwait(false);
        _raw = _fx.RawConnection;
        await _fx.ExecuteDdlAsync(
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER, Total NUMERIC(18,4));")
            .ConfigureAwait(false);

        // Bulk seed via a parameterless multi-VALUES INSERT keeps setup quick.
        await using var ins = _raw.CreateCommand();
        ins.CommandText = "INSERT INTO Orders (Id, CustomerId, Total) VALUES (@id, @cust, @total)";
        var pId = ins.CreateParameter(); pId.ParameterName = "@id"; ins.Parameters.Add(pId);
        var pCust = ins.CreateParameter(); pCust.ParameterName = "@cust"; ins.Parameters.Add(pCust);
        var pTotal = ins.CreateParameter(); pTotal.ParameterName = "@total"; ins.Parameters.Add(pTotal);
        for (var i = 1; i <= RowCount; i++)
        {
            pId.Value = i;
            pCust.Value = 100 + (i % 50);
            pTotal.Value = 9.99m + i;
            await ins.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        _repo = new PostgresMultiRowRepository(_fx.Connection);
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _fx.DisposeAsync().ConfigureAwait(false);

    [Benchmark(Baseline = true)]
    public async Task<List<OrderRow>> HandWrittenAdoNet()
    {
        var list = new List<OrderRow>(capacity: RowCount);
        await using var cmd = _raw.CreateCommand();
        cmd.CommandText = "SELECT Id, CustomerId, Total FROM Orders ORDER BY Id";
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            list.Add(new OrderRow(reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2)));
        }
        return list;
    }

    [Benchmark]
    public async Task<List<OrderRow>> Dapper_AOT()
    {
        var rows = await _raw.QueryAsync<OrderRow>(
            "SELECT Id, CustomerId, Total FROM Orders ORDER BY Id").ConfigureAwait(false);
        return [.. rows];
    }

    // See MultiRowReadBench — `IAsyncEnumerable<T>` is the canonical streaming
    // shape; the benchmark materializes to List<T> for parity with the other
    // implementations.
    [Benchmark]
    public async Task<List<OrderRow>> ZeroAlloc_ORM()
    {
        var list = new List<OrderRow>(capacity: RowCount);
        await foreach (var row in _repo.StreamAllAsync(default).ConfigureAwait(false))
        {
            list.Add(row);
        }
        return list;
    }
}

public sealed partial class PostgresMultiRowRepository(IAsyncDbConnection connection)
{
    [Query("SELECT Id, CustomerId, Total FROM Orders ORDER BY Id")]
    public partial IAsyncEnumerable<OrderRow> StreamAllAsync(
        [EnumeratorCancellation] CancellationToken ct);
}
