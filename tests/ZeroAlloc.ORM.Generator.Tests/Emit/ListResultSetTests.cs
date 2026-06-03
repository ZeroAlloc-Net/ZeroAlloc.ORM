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

    [Fact]
    public Task ListResultSet_Task_List_emits_buffered_list_shape()
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
                public partial Task<List<OrderListRow>> ListOrdersAsync(
                    int limit, int offset, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task ListResultSet_Task_IList_emits_buffered_list_shape()
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
                public partial Task<IList<OrderListRow>> ListOrdersAsync(
                    int limit, int offset, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task ListResultSet_FlatRow_with_NonNullable_Composite_emits_recursed_construction()
    {
        // v1.6 — Task<IReadOnlyList<OrderRow>> where OrderRow has a non-nullable
        // Money composite column. Expects emit to include
        // `new global::TestApp.Money(__reader.GetDecimal(1), __reader.GetString(2))`
        // inside the row construction.
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record Money(decimal Amount, string Currency);
            public sealed record OrderRow(int Id, Money Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Orders ORDER BY Id")]
                public partial Task<IReadOnlyList<OrderRow>> ListOrdersAsync(CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task ListResultSet_DomainEntity_with_NonNullable_Composite_emits_recursed_construction()
    {
        // v1.6 — same as above but DomainEntity shape (column-name path uses
        // hoisted ordinal locals via EmitNestedCompositeConstructionByOrdinalNameWithHoisted).
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record Money(decimal Amount, string Currency);
            public sealed class Order
            {
                public Order(int Id, Money Total) { this.Id = Id; this.Total = Total; }
                public int Id { get; }
                public Money Total { get; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Orders")]
                public partial Task<IReadOnlyList<Order>> ListOrdersAsync(CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task ListResultSet_with_Nullable_Composite_still_rejected()
    {
        // v1.6 — nullable composites in list rows are NOT yet supported.
        // The HasNullableCompositeColumn classifier guard still routes them
        // to ZAO022. Expect the generator output to reflect that rejection
        // (Unknown emit shape + ZAO022 diagnostic).
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record Money(decimal Amount, string Currency);
            public sealed record OrderRow(int Id, Money? Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Orders")]
                public partial Task<IReadOnlyList<OrderRow>> ListOrdersAsync(CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
