using System.Threading.Tasks;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.3 Phase B.3 — single-command ;-joined fallback snapshot. Forced via
// [Query(Batch = BatchMode.Never)] so the JoinedStatementsOnly strategy is
// selected and the snapshot captures only the joined emit path.
public class MultiResultSetJoinedTests
{
    [Fact]
    public void Tuple_with_record_and_list_emits_joined_fallback_path()
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
            "    [Query(\"SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Sku, Quantity FROM OrderLines WHERE OrderId = @id;\", Batch = BatchMode.Never)]\n" +
            "    public partial Task<(OrderRow Head, List<OrderLineRow> Lines)?> GetWithLinesAsync(int id, CancellationToken ct);\n" +
            "}\n";
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void DomainEntity_lists_sharing_column_names_resolve_ordinals_before_each_read_loop()
    {
        // #326 - each list element resolves its ordinals once, before its read
        // loop. Both result sets have an Id and a Name column, so the hoisted
        // locals carry the element's prefix to stay distinct.
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed class Customer
            {
                public Customer(int id, string name, string? nickname) { Id = id; Name = name; Nickname = nickname; }
                public int Id { get; }
                public string Name { get; }
                public string? Nickname { get; }
            }

            public sealed class Supplier
            {
                public Supplier(int id, string name) { Id = id; Name = name; }
                public int Id { get; }
                public string Name { get; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Name, Nickname FROM Customers; SELECT Id, Name FROM Suppliers;", Batch = BatchMode.Never)]
                public partial Task<(IReadOnlyList<Customer> Customers, IReadOnlyList<Supplier> Suppliers)?> GetAllAsync(CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }
}
