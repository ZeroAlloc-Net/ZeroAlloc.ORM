using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// Issue #102 — Task<IReadOnlyList<TRow>> as a bare top-level return shape.
// Pre-1.2, this fell through classification to EmitShape.Unknown and emitted
// the stub `// TODO: emit body for {MethodName}` comment, leaving adopters
// who wanted a paginated list endpoint with two equally-bad workarounds:
//   1. IAsyncEnumerable<T> + drain into a List in the handler (works, but
//      adds a handler-side await foreach + manual List.Add loop).
//   2. Hand-roll the SELECT with raw ADO.NET (reintroduces the hold-the-slot
//      pattern the generator was designed to eliminate).
//
// One snapshot per element-materialization path:
//   * FlatRow — positional record (the common case).
//   * DomainEntity — class with named ctor parameters (column-name-keyed reads
//     with GetOrdinal hoisting).
public class ListResultSetTests
{
    [Fact]
    public Task ListResultSet_with_FlatRow_record_element_emits_buffered_drain()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderListRow(int Id, int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, CustomerId, Total FROM Orders ORDER BY Id LIMIT @limit OFFSET @offset")]
                public partial Task<IReadOnlyList<OrderListRow>> ListOrdersAsync(
                    int limit, int offset, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task ListResultSet_with_DomainEntity_class_element_emits_named_column_reads()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed class Customer
            {
                public Customer(int Id, string Name) { this.Id = Id; this.Name = Name; }
                public int Id { get; }
                public string Name { get; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Name FROM Customers ORDER BY Id")]
                public partial Task<IReadOnlyList<Customer>> ListCustomersAsync(CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
