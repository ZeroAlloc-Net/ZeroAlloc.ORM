using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Dapper;
using Npgsql;
using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Benchmarks.Postgres;

// v0.7 Phase A.4 — Postgres variant of MultiResultSetBench. Same head + lines
// shape; Npgsql supports ;-joined multi-result-set batches natively. Gated
// behind the "Postgres" category.
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[BenchmarkCategory("Postgres")]
public class PostgresMultiResultSetBench
{
    private const int LineCount = 10;

    private PostgresBenchFixture _fx = null!;
    private NpgsqlConnection _raw = null!;
    private PostgresMultiResultSetRepository _repo = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _fx = new PostgresBenchFixture();
        await _fx.InitializeAsync().ConfigureAwait(false);
        _raw = _fx.RawConnection;
        await _fx.ExecuteDdlAsync("""
            CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER, Total NUMERIC(18,4));
            CREATE TABLE OrderLines (Id INTEGER PRIMARY KEY, OrderId INTEGER, Sku TEXT, Qty INTEGER);
            INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 100, 99.95);
            """).ConfigureAwait(false);

        await using var ins = _raw.CreateCommand();
        ins.CommandText = "INSERT INTO OrderLines (Id, OrderId, Sku, Qty) VALUES (@id, 1, @sku, @qty)";
        var pId = ins.CreateParameter(); pId.ParameterName = "@id"; ins.Parameters.Add(pId);
        var pSku = ins.CreateParameter(); pSku.ParameterName = "@sku"; ins.Parameters.Add(pSku);
        var pQty = ins.CreateParameter(); pQty.ParameterName = "@qty"; ins.Parameters.Add(pQty);
        for (var i = 1; i <= LineCount; i++)
        {
            pId.Value = i;
            pSku.Value = $"SKU-{i:D4}";
            pQty.Value = i;
            await ins.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        _repo = new PostgresMultiResultSetRepository(_fx.Connection);
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _fx.DisposeAsync().ConfigureAwait(false);

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

public sealed partial class PostgresMultiResultSetRepository(IAsyncDbConnection connection)
{
    [Query(
        "SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Id, OrderId, Sku, Qty FROM OrderLines WHERE OrderId = @id;",
        Batch = BatchMode.Auto)]
    public partial Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?> GetOrderWithLinesAsync(
        int id,
        CancellationToken ct);
}
