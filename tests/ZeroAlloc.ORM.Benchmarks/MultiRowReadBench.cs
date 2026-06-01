using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Data.Async;
using System.Data.Async.Adapters;
using System.Runtime.CompilerServices;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Benchmarks;

// v0.7 Phase A.3 — multi-row read workload. SELECTs 1000 rows into a
// List<OrderRow>, isolating the per-row reader-tape + materializer cost
// amortised over a meaningful row count. Same three implementations as
// SingleRowReadBench (hand-written / Dapper.AOT / ZA.ORM).
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class MultiRowReadBench
{
    private const int RowCount = 1000;

    private SqliteConnection _raw = null!;
    private IAsyncDbConnection _conn = null!;
    private MultiRowRepository _repo = null!;

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

        // Seed 1000 rows in a single transaction for fast setup.
        await using var tx = (SqliteTransaction)await _raw.BeginTransactionAsync().ConfigureAwait(false);
        var ins = _raw.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO Orders (Id, CustomerId, Total) VALUES ($id, $cust, $total)";
        var pId = ins.CreateParameter(); pId.ParameterName = "$id"; ins.Parameters.Add(pId);
        var pCust = ins.CreateParameter(); pCust.ParameterName = "$cust"; ins.Parameters.Add(pCust);
        var pTotal = ins.CreateParameter(); pTotal.ParameterName = "$total"; ins.Parameters.Add(pTotal);
        await using (ins.ConfigureAwait(false))
        {
            for (var i = 1; i <= RowCount; i++)
            {
                pId.Value = i;
                pCust.Value = 100 + (i % 50);
                pTotal.Value = 9.99m + i;
                await ins.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
        await tx.CommitAsync().ConfigureAwait(false);

        _repo = new MultiRowRepository(_conn);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _conn.DisposeAsync().ConfigureAwait(false);
        await _raw.DisposeAsync().ConfigureAwait(false);
    }

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

    // Dapper's natural pattern returns IEnumerable<T>; .AsList() unwraps the
    // underlying list without re-copying when possible, matching the parity of
    // the hand-written / ZA.ORM pre-sized lists. Using `[.. rows]` would force
    // a fresh collection-expression spread and pay an unfair allocation tax.
    [Benchmark]
    public async Task<List<OrderRow>> Dapper_AOT()
    {
        var rows = await _raw.QueryAsync<OrderRow>(
            "SELECT Id, CustomerId, Total FROM Orders ORDER BY Id").ConfigureAwait(false);
        return rows.AsList();
    }

    // ZA.ORM v0.7 does not support `Task<List<T>>` as a top-level return shape —
    // `IAsyncEnumerable<T>` is the canonical streaming form. The benchmark
    // materializes to List<T> so the comparison stays apples-to-apples.
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

public sealed partial class MultiRowRepository(IAsyncDbConnection connection)
{
    [Query("SELECT Id, CustomerId, Total FROM Orders ORDER BY Id")]
    public partial IAsyncEnumerable<OrderRow> StreamAllAsync(
        [EnumeratorCancellation] CancellationToken ct);
}
