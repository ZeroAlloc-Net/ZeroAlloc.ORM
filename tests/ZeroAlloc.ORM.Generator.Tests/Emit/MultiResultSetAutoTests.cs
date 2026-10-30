using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.3 Phase B.4 — BatchWithFallback runtime branch snapshot. Uses the default
// [Query] form (BatchMode.Auto) so the resolver picks BatchWithFallback and the
// emit produces a single body that branches on __conn.CanCreateBatch at runtime.
public class MultiResultSetAutoTests
{
    [Fact]
    public void Tuple_with_record_and_list_emits_runtime_CanCreateBatch_branch()
    {
        var source =
            "using System.Collections.Generic;\n" +
            "using System.Data.Async;\n" +
            "using System.Threading;\n" +
            "using System.Threading.Tasks;\n" +
            "using ZeroAlloc.ORM;\n" +
            "\n" +
            "namespace TestApp;\n" +
            "\n" +
            "public sealed record OrderRow(int Id, int CustomerId, decimal Total);\n" +
            "public sealed record OrderLineRow(string Sku, int Quantity);\n" +
            "\n" +
            "public sealed partial class Repo(IAsyncDbConnection connection)\n" +
            "{\n" +
            "    [Query(\"SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Sku, Quantity FROM OrderLines WHERE OrderId = @id;\")]\n" +
            "    public partial Task<(OrderRow Head, List<OrderLineRow> Lines)?> GetWithLinesAsync(int id, CancellationToken ct);\n" +
            "}\n";
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    // #427 — on a PostgreSQL repository Auto runs one `;`-joined command: Npgsql's batch cost
    // more bytes per call and was not faster. Always keeps the batch, and every other dialect
    // keeps the runtime branch on CanCreateBatch.
    [Theory]
    [InlineData("PostgreSql", "Auto", "joined")]
    [InlineData("PostgreSql", "Never", "joined")]
    [InlineData("PostgreSql", "Always", "batch")]
    [InlineData("SqlServer", "Auto", "branch")]
    [InlineData("Sqlite", "Auto", "branch")]
    public void Auto_on_PostgreSql_runs_one_joined_command(string dialect, string batchMode, string expected)
    {
        var source =
            "using System.Collections.Generic;\n" +
            "using System.Data.Async;\n" +
            "using System.Threading;\n" +
            "using System.Threading.Tasks;\n" +
            "using ZeroAlloc.ORM;\n" +
            "\n" +
            "namespace TestApp;\n" +
            "\n" +
            "public sealed record OrderRow(int Id, int CustomerId, decimal Total);\n" +
            "public sealed record OrderLineRow(string Sku, int Quantity);\n" +
            "\n" +
            "[Dialect(SqlDialect." + dialect + ")]\n" +
            "public sealed partial class Repo(IAsyncDbConnection connection)\n" +
            "{\n" +
            "    [Query(\"SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Sku, Quantity FROM OrderLines WHERE OrderId = @id;\", Batch = BatchMode." + batchMode + ")]\n" +
            "    public partial Task<(OrderRow Head, List<OrderLineRow> Lines)?> GetWithLinesAsync(int id, CancellationToken ct);\n" +
            "}\n";

        var result = GeneratorHarness.RunGenerator(source);
        Assert.Empty(result.Diagnostics);
        var generated = string.Concat(result.GeneratedTrees.Select(t => t.ToString()));
        var branches = generated.Contains("if (__conn.CanCreateBatch)", StringComparison.Ordinal);
        var batches = generated.Contains("__conn.CreateBatch()", StringComparison.Ordinal);
        var joined = generated.Contains("await using var __cmd = __conn.CreateCommand();", StringComparison.Ordinal);
        Assert.Equal(expected, (branches, batches, joined) switch
        {
            (true, true, true) => "branch",
            (false, true, false) => "batch",
            (false, false, true) => "joined",
            _ => $"branch={branches} batch={batches} joined={joined}",
        });
    }
}
