using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Data.Async;
using System.Data.Async.Adapters;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Benchmarks;

// v0.7 Phase A.3 — multi-result-set workload. One execution returns a
// header row + N detail rows in a single (;-joined) command. Matches the
// canonical head + lines pattern from MultiResultSetRepo in the integration
// suite. Measures the cost of NextResultAsync + a second materializer pass.
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class MultiResultSetBench
{
    private const int LineCount = 10;

    private SqliteConnection _raw = null!;
    private IAsyncDbConnection _conn = null!;
    private MultiResultSetRepository _repo = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _raw = new SqliteConnection("Data Source=:memory:");
        _conn = _raw.AsAsync();
        await _conn.OpenAsync().ConfigureAwait(false);

        var ddl = _conn.CreateCommand();
        await using (ddl.ConfigureAwait(false))
        {
            ddl.CommandText = """
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER, Total REAL);
                CREATE TABLE OrderLines (Id INTEGER PRIMARY KEY, OrderId INTEGER, Sku TEXT, Qty INTEGER);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 100, 99.95);
                """;
            await ddl.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var ins = _raw.CreateCommand();
        ins.CommandText = "INSERT INTO OrderLines (Id, OrderId, Sku, Qty) VALUES ($id, 1, $sku, $qty)";
        var pId = ins.CreateParameter(); pId.ParameterName = "$id"; ins.Parameters.Add(pId);
        var pSku = ins.CreateParameter(); pSku.ParameterName = "$sku"; ins.Parameters.Add(pSku);
        var pQty = ins.CreateParameter(); pQty.ParameterName = "$qty"; ins.Parameters.Add(pQty);
        await using (ins.ConfigureAwait(false))
        {
            for (var i = 1; i <= LineCount; i++)
            {
                pId.Value = i;
                pSku.Value = $"SKU-{i:D4}";
                pQty.Value = i;
                await ins.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        _repo = new MultiResultSetRepository(_conn);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _conn.DisposeAsync().ConfigureAwait(false);
        await _raw.DisposeAsync().ConfigureAwait(false);
    }

    [Benchmark(Baseline = true)]
    public async Task<(OrderRow Head, List<OrderLineRow> Lines)?> HandWrittenAdoNet()
    {
        await using var cmd = _raw.CreateCommand();
        cmd.CommandText = """
            SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id;
            SELECT Id, OrderId, Sku, Qty FROM OrderLines WHERE OrderId = @id;
            """;
        var p = cmd.CreateParameter(); p.ParameterName = "@id"; p.Value = 1; cmd.Parameters.Add(p);
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            return null;
        }
        var head = new OrderRow(reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2));
        await reader.NextResultAsync().ConfigureAwait(false);
        var lines = new List<OrderLineRow>(capacity: LineCount);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            lines.Add(new OrderLineRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3)));
        }
        return (head, lines);
    }

    // Dapper.AOT supports multi-result via QueryMultipleAsync. For an apples-
    // to-apples comparison we materialize the same shape (head + line list).
    [Benchmark]
    public async Task<(OrderRow Head, List<OrderLineRow> Lines)?> Dapper_AOT()
    {
        using var multi = await _raw.QueryMultipleAsync(
            """
            SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id;
            SELECT Id, OrderId, Sku, Qty FROM OrderLines WHERE OrderId = @id;
            """,
            new { id = 1 }).ConfigureAwait(false);
        var head = await multi.ReadFirstOrDefaultAsync<OrderRow>().ConfigureAwait(false);
        if (head is null)
        {
            return null;
        }
        var lines = (await multi.ReadAsync<OrderLineRow>().ConfigureAwait(false)).AsList();
        return (head, lines);
    }

    [Benchmark]
    public Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?> ZeroAlloc_ORM()
        => _repo.GetOrderWithLinesAsync(1, default);
}

public sealed partial class MultiResultSetRepository(IAsyncDbConnection connection)
{
    [Query(
        "SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Id, OrderId, Sku, Qty FROM OrderLines WHERE OrderId = @id;",
        Batch = BatchMode.Auto)]
    public partial Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?> GetOrderWithLinesAsync(
        int id,
        CancellationToken ct);
}

public sealed record OrderLineRow(int Id, int OrderId, string Sku, int Qty);
