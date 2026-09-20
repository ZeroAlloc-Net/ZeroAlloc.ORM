using System.Threading.Tasks;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v1.5 — IAsyncDbTransaction parameter support.
// Four snapshots cover the headline emit shapes:
//   * [Command(Kind = NonQuery)] — the dominant write shape
//   * [Command(Kind = Identity)] — RETURNING-Id case
//   * [Query] returning Task<T?> (FlatRow) — single-row read
//   * [Command(Kind = BulkInsert)] — confirms the per-chunk command picks up the tx line
public class TransactionParameterTests
{
    [Fact]
    public void NonQuery_with_transaction_parameter_emits_cmd_Transaction_assignment()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET Status = @status WHERE Id = @id", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateStatusAsync(int id, string status, IAsyncDbTransaction tx, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Identity_with_transaction_parameter_emits_cmd_Transaction_assignment()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId) VALUES (@customerId) RETURNING Id", Kind = CommandKind.Identity)]
                public partial Task<int> InsertOrderAsync(int customerId, IAsyncDbTransaction tx, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Query_FlatRow_with_transaction_parameter_emits_cmd_Transaction_assignment()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, CustomerId FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> ReadOrderAsync(int id, IAsyncDbTransaction tx, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void BulkInsert_with_transaction_parameter_emits_cmd_Transaction_per_chunk()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertOrdersAsync(IReadOnlyList<OrderRow> orders, IAsyncDbTransaction tx, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }
}
