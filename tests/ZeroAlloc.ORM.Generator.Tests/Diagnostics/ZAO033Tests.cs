using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// ZAO033 is the mirror of ZAO032 — fires when the SQL contains MORE statements than
// the tuple has elements. The reader would leave result sets unconsumed and the
// extra SELECTs silently waste server work; surfacing the mismatch forces the
// adopter to either drop the extra SQL or widen the tuple to capture all rows.
public class ZAO033Tests
{
    [Fact]
    public void Statement_count_greater_than_tuple_arity_emits_ZAO033()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);
            public sealed record OrderLineRow(string Sku, int Quantity);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Sku, Quantity FROM OrderLines WHERE OrderId = @id; SELECT 1;")]
                public partial Task<(OrderRow Head, List<OrderLineRow> Lines)> GetWithLinesAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        var match = diagnostics.AsEnumerable().First(d => string.Equals(d.Id, "ZAO033", System.StringComparison.Ordinal));
        var message = match.GetMessage(CultureInfo.InvariantCulture);
        // Anchor on the descriptor's actual phrasing — "{N} SQL statements" and
        // "{N} elements" — so partial digit matches elsewhere in the message
        // can't accidentally pass.
        Assert.Contains("3 SQL statements", message, System.StringComparison.Ordinal);
        Assert.Contains("2 elements", message, System.StringComparison.Ordinal);

        // Diagnostic must carry a real source location pointing at the method
        // identifier — adopters rely on this to navigate from the squiggle.
        Assert.NotEqual(Location.None, match.Location);
        var span = match.Location.GetLineSpan();
        Assert.True(span.IsValid);
    }

    [Fact]
    public void Two_element_tuple_with_five_statements_emits_ZAO033()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1; SELECT 2; SELECT 3; SELECT 4; SELECT 5;")]
                public partial Task<(int A, int B)> GetTwoAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO033", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Tuple_arity_matching_statement_count_does_not_emit_ZAO033()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);
            public sealed record OrderLineRow(string Sku, int Quantity);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Sku, Quantity FROM OrderLines WHERE OrderId = @id;")]
                public partial Task<(OrderRow Head, List<OrderLineRow> Lines)> GetWithLinesAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO033", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Tuple_arity_greater_than_statement_count_does_not_emit_ZAO033()
    {
        // Inverse case: arity 2, count 1 — this is ZAO032's territory. ZAO033 must
        // stay quiet so the adopter only sees one actionable diagnostic.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial Task<(int A, int B)> GetTwoAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO033", System.StringComparison.Ordinal));
    }
}
