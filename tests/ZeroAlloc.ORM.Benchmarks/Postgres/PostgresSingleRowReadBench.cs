using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Dapper;
using Npgsql;
using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Benchmarks.Postgres;

// v0.7 Phase A.4 — Postgres variant of SingleRowReadBench. Uses Testcontainers
// to boot postgres:16-alpine. Gated behind the "Postgres" category so the
// default `dotnet run -c Release` invocation skips it. Run locally with:
//     dotnet run -c Release -- --filter "*Postgres*"
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[BenchmarkCategory("Postgres")]
public class PostgresSingleRowReadBench
{
    private PostgresBenchFixture _fx = null!;
    private NpgsqlConnection _raw = null!;
    private PostgresOrderRepository _repo = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _fx = new PostgresBenchFixture();
        await _fx.InitializeAsync().ConfigureAwait(false);
        _raw = _fx.RawConnection;
        await _fx.ExecuteDdlAsync("""
            CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER, Total NUMERIC(18,4));
            INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 100, 99.95);
            """).ConfigureAwait(false);
        _repo = new PostgresOrderRepository(_fx.Connection);
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _fx.DisposeAsync().ConfigureAwait(false);

    [Benchmark(Baseline = true)]
    public async Task<OrderRow?> HandWrittenAdoNet()
    {
        await using var cmd = _raw.CreateCommand();
        cmd.CommandText = "SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id";
        var p = cmd.CreateParameter(); p.ParameterName = "@id"; p.Value = 1; cmd.Parameters.Add(p);
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            return null;
        }
        return new OrderRow(reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2));
    }

    [Benchmark]
    public Task<OrderRow?> Dapper_AOT()
        => _raw.QueryFirstOrDefaultAsync<OrderRow>(
            "SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id",
            new { id = 1 });

    [Benchmark]
    public Task<OrderRow?> ZeroAlloc_ORM() => _repo.GetByIdAsync(1, default);
}

public sealed partial class PostgresOrderRepository(IAsyncDbConnection connection)
{
    [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
    public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
}
