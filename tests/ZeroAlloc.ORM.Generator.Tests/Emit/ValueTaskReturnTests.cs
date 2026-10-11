using System.Linq;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// #331: ValueTask<T> is accepted wherever Task<T> is, so a query that completes
// synchronously, as every Sqlite query does, need not allocate a Task. The emitted
// body is the same `async` body; only the declared return type differs.
public class ValueTaskReturnTests
{
    private const string Usings = """
        using System.Collections.Generic;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        public sealed record OrderRow(int Id, decimal Total);
        public sealed record OrderLineRow(int OrderId, string Sku);

        public sealed class Customer
        {
            public Customer(int id, string name) { Id = id; Name = name; }
            public int Id { get; }
            public string Name { get; }
        }

        """;

    [Fact]
    public void ValueTask_query_of_every_shape_compiles_without_diagnostics()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT COUNT(*) FROM Orders")]
                public partial ValueTask<int> CountAsync(CancellationToken ct);

                [Query("SELECT MAX(Id) FROM Orders")]
                public partial ValueTask<long?> MaxIdAsync(CancellationToken ct);

                [Query("SELECT Name FROM Customers WHERE Id = @id")]
                public partial ValueTask<string?> NameAsync(int id, CancellationToken ct);

                [Query("SELECT Id, Total FROM Orders WHERE Id = @id")]
                public partial ValueTask<OrderRow?> GetAsync(int id, CancellationToken ct);

                [Query("SELECT Id, Name FROM Customers WHERE Id = @id")]
                public partial ValueTask<Customer?> GetCustomerAsync(int id, CancellationToken ct);

                [Query("SELECT Id, Total FROM Orders")]
                public partial ValueTask<IReadOnlyList<OrderRow>> ListAsync(CancellationToken ct);

                [Query("SELECT Id, Total FROM Orders WHERE Id = @id; SELECT OrderId, Sku FROM Lines WHERE OrderId = @id")]
                public partial ValueTask<(OrderRow? Order, IReadOnlyList<OrderLineRow> Lines)> GetWithLinesAsync(int id, CancellationToken ct);

                [StoredProcedure("usp_CountOrders")]
                public partial ValueTask<int> CountViaProcAsync(CancellationToken ct);

                [Command("INSERT INTO Orders (Id, Total) VALUES (@Id, @Total)", Kind = CommandKind.BulkInsert)]
                public partial ValueTask<int> InsertAsync(IReadOnlyList<OrderRow> rows, CancellationToken ct);

                [Command("INSERT INTO Orders (Id, Total) VALUES (@Id, @Total) RETURNING Id", Kind = CommandKind.BulkInsert)]
                public partial ValueTask<IReadOnlyList<int>> InsertReturningAsync(IReadOnlyList<OrderRow> rows, CancellationToken ct);
            }
            """;

        var (runResult, compilation) = GeneratorHarness.RunGeneratorAndGetCompilation(source);
        runResult.Diagnostics.Select(d => d.ToString()).Should().BeEmpty();
        compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .Should().BeEmpty();
    }

    [Fact]
    public void ValueTask_scalar_query_emits_an_async_ValueTask_body()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT COUNT(*) FROM Orders")]
                public partial ValueTask<int> CountAsync(CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void ValueTask_FlatRow_query_emits_an_async_ValueTask_body()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Total FROM Orders WHERE Id = @id")]
                public partial ValueTask<OrderRow?> GetAsync(int id, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void ValueTask_BulkInsert_emits_an_async_ValueTask_body()
    {
        var source = Usings + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (Id, Total) VALUES (@Id, @Total)", Kind = CommandKind.BulkInsert)]
                public partial ValueTask<int> InsertAsync(IReadOnlyList<OrderRow> rows, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }
}
