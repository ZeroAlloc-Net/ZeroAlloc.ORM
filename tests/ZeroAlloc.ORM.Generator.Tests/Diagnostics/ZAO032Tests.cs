using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// ZAO032 guards against MultiResultSet tuple arity exceeding the number of SQL
// statements. The shape detection happily accepts a 3-element tuple, but if the SQL
// only contains 2 statements the runtime would read past the last result set and
// throw at runtime. ZAO032 surfaces the mismatch at generation time so the adopter
// either adds the missing SELECT or shrinks the tuple.
public class ZAO032Tests
{
    [Fact]
    public void Tuple_arity_greater_than_statement_count_emits_ZAO032()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial Task<(int A, int B, int C)> GetThreeAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        var match = diagnostics.AsEnumerable().First(d => string.Equals(d.Id, "ZAO032", System.StringComparison.Ordinal));
        var message = match.GetMessage(CultureInfo.InvariantCulture);
        // Anchor on the descriptor's actual phrasing — "{N}-element tuple" and
        // "{N} statement(s)" — so partial digit matches elsewhere in the message
        // can't accidentally pass.
        Assert.Contains("3-element tuple", message, System.StringComparison.Ordinal);
        Assert.Contains("1 statement", message, System.StringComparison.Ordinal);

        // Diagnostic must carry a real source location pointing at the method
        // identifier — adopters rely on this to navigate from the squiggle.
        Assert.NotEqual(Location.None, match.Location);
        var span = match.Location.GetLineSpan();
        Assert.True(span.IsValid);
    }

    [Fact]
    public void Tuple_arity_three_with_two_statements_emits_ZAO032()
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
                public partial Task<(OrderRow Head, List<OrderLineRow> Lines, int Count)> GetWithLinesAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO032", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Tuple_arity_matching_statement_count_does_not_emit_ZAO032()
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

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO032", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Single_FlatRow_return_does_not_emit_ZAO032()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO032", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Statement_count_greater_than_tuple_arity_does_not_emit_ZAO032()
    {
        // Symmetric to ZAO033's reverse-direction guard test. When the SQL has
        // MORE statements than the tuple has elements, ZAO033 fires — but ZAO032
        // must stay silent so the adopter sees exactly one actionable diagnostic.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1; SELECT 2; SELECT 3;")]
                public partial Task<(int A, int B)> GetTwoAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO032", System.StringComparison.Ordinal));
    }
}
