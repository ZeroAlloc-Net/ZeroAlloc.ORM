using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// Every ZAO diagnostic is reported at the attribute, type, method, return type, parameter or
// tuple element it is about, as a location in the user's syntax tree: the IDE can point at it
// and #pragma warning disable can suppress it. A location rebuilt from the file path alone is
// an external-file location, which #pragma cannot reach.
//
// A source marked with [| and |] gives the expected span; the markers are removed before it
// runs. ZAO060 is reserved and never reported, so it has no case here.
public class DiagnosticLocationTests
{
    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using System.Data;
        using System.Data.Async;
        using System.Threading;
        using System.Threading.Tasks;
        using ZeroAlloc.ORM;

        namespace TestApp;

        """;

    [Theory]
    // ZAO001: the method that is not partial.
    [InlineData("ZAO001", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT 1")]
            public Task<int> [|GetOneAsync|](CancellationToken ct) => throw null!;
        }
        """)]
    // ZAO002: the unsupported return type.
    [InlineData("ZAO002", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT 1")]
            public partial [|int|] GetOneBad(CancellationToken ct);
        }
        """)]
    // ZAO002, scalar command with an element type that has no scalar reader: the return type.
    [InlineData("ZAO002", """
        public sealed class Widget { }
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Command("SELECT W FROM Widgets", Kind = CommandKind.Scalar)]
            public partial [|Task<Widget>|] GetWidgetAsync(CancellationToken ct);
        }
        """)]
    // ZAO003: the type that has no connection.
    [InlineData("ZAO003", """
        public sealed partial class [|Repo|]
        {
            [Query("SELECT 1")]
            public partial Task<int> GetOneAsync(CancellationToken ct);
        }
        """)]
    // ZAO004: the type that is not partial.
    [InlineData("ZAO004", """
        public sealed class [|Repo|](IAsyncDbConnection connection)
        {
            [Query("SELECT 1")]
            public partial Task<int> GetOneAsync(CancellationToken ct);
        }
        """)]
    // ZAO005: the method that carries two pipeline attributes.
    [InlineData("ZAO005", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT 1")]
            [Command("UPDATE X SET A = 1", Kind = CommandKind.NonQuery)]
            public partial Task<int> [|DoSomethingAsync|](CancellationToken ct);
        }
        """)]
    // ZAO006: the method with two cancellation tokens.
    [InlineData("ZAO006", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT 1")]
            public partial Task<int> [|GetOneAsync|](CancellationToken a, CancellationToken b);
        }
        """)]
    // ZAO007: the streaming method without [EnumeratorCancellation].
    [InlineData("ZAO007", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT 1")]
            public partial IAsyncEnumerable<int> [|StreamAsync|](CancellationToken ct);
        }
        """)]
    // ZAO008: the single-result method whose SQL has several statements.
    [InlineData("ZAO008", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT 1; SELECT 2")]
            public partial Task<int> [|GetOneAsync|](CancellationToken ct);
        }
        """)]
    // ZAO009: the redundant async modifier.
    [InlineData("ZAO009", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT 1")]
            public partial [|async|] Task<int> GetOneAsync(CancellationToken ct);
        }
        """)]
    // ZAO020: the method that asks for SQL from a resource.
    [InlineData("ZAO020", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("MyApp.Sql.GetOne", FromResource = true)]
            public partial Task<int> [|GetOneAsync|](CancellationToken ct);
        }
        """)]
    // ZAO022: the return type of unknown shape.
    [InlineData("ZAO022", """
        public sealed record OrderRow(int Id);
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT id FROM orders")]
            public partial [|Task<HashSet<OrderRow>>|] GetOrdersAsync(CancellationToken ct);
        }
        """)]
    // ZAO032: the method whose tuple has more elements than the SQL has statements.
    [InlineData("ZAO032", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT 1")]
            public partial Task<(int A, int B, int C)> [|GetThreeAsync|](CancellationToken ct);
        }
        """)]
    // ZAO033: the method whose SQL has more statements than the tuple has elements.
    [InlineData("ZAO033", """
        public sealed record OrderRow(int Id, int CustomerId, decimal Total);
        public sealed record OrderLineRow(string Sku, int Quantity);
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id; SELECT Sku, Quantity FROM OrderLines WHERE OrderId = @id; SELECT 1;")]
            public partial Task<(OrderRow Head, List<OrderLineRow> Lines)> [|GetWithLinesAsync|](int id, CancellationToken ct);
        }
        """)]
    // ZAO040: the return type that has no construction strategy.
    [InlineData("ZAO040", """
        public sealed class Mystery { }
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT 1 FROM Mysteries LIMIT 1")]
            public partial [|Task<Mystery?>|] GetAsync(CancellationToken ct);
        }
        """)]
    // ZAO041: the parameter that has no unwrap strategy.
    [InlineData("ZAO041", """
        public sealed class Filter { }
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT 1 FROM Orders WHERE Filter = @filter")]
            public partial Task<int> CountAsync(Filter [|filter|], CancellationToken ct);
        }
        """)]
    // ZAO042: the [StoreAsString] attribute on a type that is not an enum.
    [InlineData("ZAO042", """
        [[|StoreAsString|]]
        public sealed class NotAnEnum { }
        """)]
    // ZAO043: the return type whose [Materialize] factory does not exist.
    [InlineData("ZAO043", """
        [Materialize(Factory = "FromStorage")]
        public readonly record struct Money(decimal Amount, string Currency);
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
            public partial [|Task<Money>|] GetTotalAsync(int id, CancellationToken ct);
        }
        """)]
    // ZAO044: the return type whose [Materialize] factory is ambiguous.
    [InlineData("ZAO044", """
        [Materialize(Factory = "FromStorage")]
        public readonly record struct Money(decimal Amount, string Currency)
        {
            public static Money FromStorage(string amountText, string currency)
                => new Money(decimal.Parse(amountText, global::System.Globalization.CultureInfo.InvariantCulture), currency);
            public static Money FromStorage(decimal amount, string currency) => new Money(amount, currency);
        }
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
            public partial [|Task<Money>|] GetTotalAsync(int id, CancellationToken ct);
        }
        """)]
    // ZAO050: the return type with a nullable composite.
    [InlineData("ZAO050", """
        public readonly record struct Money(decimal Amount, string Currency);
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
            public partial [|Task<Money?>|] GetTotalAsync(int id, CancellationToken ct);
        }
        """)]
    // ZAO051: the return type whose factory parameter matches no column.
    [InlineData("ZAO051", """
        [Materialize(Factory = "FromStorage")]
        public readonly record struct Money(decimal Amount, string Currency)
        {
            public static Money FromStorage(string rawAmount, string currency)
                => new Money(decimal.Parse(rawAmount, global::System.Globalization.CultureInfo.InvariantCulture), currency);
        }
        public sealed class Cart
        {
            public Cart(int id, Money total) { Id = id; Total = total; }
            public int Id { get; }
            public Money Total { get; }
        }
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT Id, Amount, Currency FROM Carts WHERE Id = @id")]
            public partial [|Task<Cart?>|] GetByIdAsync(int id, CancellationToken ct);
        }
        """)]
    // ZAO052: the return type with a nested composite.
    [InlineData("ZAO052", """
        public readonly record struct Inner(decimal A, string B);
        public readonly record struct Outer(int Id, Inner Inner);
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT Id, A, B FROM Things WHERE Id = @id")]
            public partial [|Task<Outer>|] GetOuterAsync(int id, CancellationToken ct);
        }
        """)]
    // ZAO061: the stored procedure method with an empty name.
    [InlineData("ZAO061", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [StoredProcedure("   ")]
            public partial Task<int> [|WhitespaceNameAsync|](CancellationToken ct);
        }
        """)]
    // ZAO062: the tuple element that matches no parameter.
    [InlineData("ZAO062", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [StoredProcedure("usp_InsertOrder")]
            public partial Task<(int Status, [|int Total|], int NewOrderId)> InsertAsync(
                int customerId, int newOrderId, CancellationToken ct);
        }
        """)]
    // ZAO063: the composite parameter with [Param(Name)].
    [InlineData("ZAO063", """
        public readonly record struct Money(decimal Amount, string Currency);
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Command("UPDATE Orders SET Amount = @custom_Amount, Currency = @custom_Currency WHERE Id = 1", Kind = CommandKind.NonQuery)]
            public partial Task<int> UpdateTotalAsync([Param(Name = "@custom")] Money [|total|], CancellationToken ct);
        }
        """)]
    // ZAO064: the stored procedure method with a Batch mode.
    [InlineData("ZAO064", """
        public sealed record OrderRow(int Id, int CustomerId);
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [StoredProcedure("usp_GetOrder", Batch = BatchMode.Always)]
            public partial Task<OrderRow?> [|GetOrderAsync|](int id, CancellationToken ct);
        }
        """)]
    // ZAO065: the decimal output parameter without a scale.
    [InlineData("ZAO065", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, decimal Total)> RunAsync(int id, decimal [|total|], CancellationToken ct);
        }
        """)]
    // ZAO066: the parameter whose facet does not apply.
    [InlineData("ZAO066", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, int Status)> RunAsync(
                int id, int status, [Param(Direction = ParameterDirection.Output)] int [|extra|], CancellationToken ct);
        }
        """)]
    // ZAO067: the return-value parameter that is not an int.
    [InlineData("ZAO067", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, long Status)> RunAsync(
                int id, [Param(Direction = ParameterDirection.ReturnValue)] long [|status|], CancellationToken ct);
        }
        """)]
    // ZAO068: the second return-value parameter.
    [InlineData("ZAO068", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [StoredProcedure("usp_X")]
            public partial Task<(int Id, int First, int Second)> RunAsync(
                int id,
                [Param(Direction = ParameterDirection.ReturnValue)] int first,
                [Param(Direction = ParameterDirection.ReturnValue)] int [|second|],
                CancellationToken ct);
        }
        """)]
    // ZAO070: the bulk insert method without a collection parameter.
    [InlineData("ZAO070", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Command("INSERT INTO X (A) VALUES (@A)", Kind = CommandKind.BulkInsert)]
            public partial Task<int> [|InsertAsync|](int a, CancellationToken ct);
        }
        """)]
    // ZAO071: the bulk insert method whose SQL has no VALUES tuple.
    [InlineData("ZAO071", """
        public sealed record OrderRow(int CustomerId, decimal Total);
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Command("INSERT INTO Orders (CustomerId, Total) SELECT 1, 2", Kind = CommandKind.BulkInsert)]
            public partial Task<int> [|InsertAsync|](IReadOnlyList<OrderRow> orders, CancellationToken ct);
        }
        """)]
    // ZAO072: the bulk insert method with a placeholder no property resolves.
    [InlineData("ZAO072", """
        public sealed record OrderRow(int Foo);
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Command("INSERT INTO Orders (Bar) VALUES (@Bar)", Kind = CommandKind.BulkInsert)]
            public partial Task<int> [|InsertAsync|](IReadOnlyList<OrderRow> orders, CancellationToken ct);
        }
        """)]
    // ZAO073: the bulk insert return type of the wrong shape.
    [InlineData("ZAO073", """
        public sealed record OrderRow(int CustomerId);
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Command("INSERT INTO Orders (CustomerId) VALUES (@CustomerId)", Kind = CommandKind.BulkInsert)]
            public partial [|Task<string>|] InsertAsync(IReadOnlyList<OrderRow> orders, CancellationToken ct);
        }
        """)]
    // ZAO074: the method that pairs [Query] with a bulk insert [Command].
    [InlineData("ZAO074", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Query("SELECT 1")]
            [Command("INSERT INTO X (A) VALUES (@A)", Kind = CommandKind.BulkInsert)]
            public partial Task<int> [|RunAsync|](CancellationToken ct);
        }
        """)]
    // ZAO080: the method with two transaction parameters.
    [InlineData("ZAO080", """
        public sealed partial class Repo(IAsyncDbConnection connection)
        {
            [Command("UPDATE X SET A = @a WHERE Id = @id", Kind = CommandKind.NonQuery)]
            public partial Task<int> [|UpdateAsync|](int id, int a, IAsyncDbTransaction tx1, IAsyncDbTransaction tx2, CancellationToken ct);
        }
        """)]
    // ZAO081: the containing type that is not partial.
    [InlineData("ZAO081", """
        public class [|Data|]
        {
            public partial class OrderRepository(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial Task<int> GetOneAsync(CancellationToken ct);
            }
        }
        """)]
    public void Diagnostic_is_reported_at_its_source_location(string id, string markedSource)
    {
        var (source, span) = Unmark(Prelude + markedSource);

        var (result, tree) = GeneratorHarness.RunGeneratorOnFile(source);

        var matching = result.Diagnostics
            .Where(d => string.Equals(d.Id, id, StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(matching);
        Assert.All(matching, d => AssertAt(d.Location, tree, span));
    }

    [Fact]
    public void Pragma_around_one_method_suppresses_that_diagnostic_only()
    {
        // Both methods report ZAO009, a warning; the pragma covers only the first.
        var source = Prelude + """
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
            #pragma warning disable ZAO009
                [Query("SELECT 1")]
                public partial async Task<int> QuietAsync(CancellationToken ct);
            #pragma warning restore ZAO009

                [Query("SELECT 2")]
                public partial async Task<int> LoudAsync(CancellationToken ct);
            }
            """;

        var (result, _) = GeneratorHarness.RunGeneratorOnFile(source);

        var zao009 = result.Diagnostics
            .Where(d => string.Equals(d.Id, "ZAO009", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, zao009.Count);
        Assert.True(ForMethod(zao009, "QuietAsync").IsSuppressed);
        Assert.False(ForMethod(zao009, "LoudAsync").IsSuppressed);

        static Diagnostic ForMethod(List<Diagnostic> diagnostics, string method) =>
            Assert.Single(diagnostics, d => d.GetMessage(CultureInfo.InvariantCulture)
                .Contains("'" + method + "'", StringComparison.Ordinal));
    }

    internal static void AssertAt(Location location, SyntaxTree tree, TextSpan expected)
    {
        Assert.True(location.IsInSource, $"Expected a source location, got {location.Kind}.");
        Assert.Same(tree, location.SourceTree);
        Assert.Equal(
            tree.GetText().ToString(expected),
            tree.GetText().ToString(location.SourceSpan));
        Assert.Equal(expected, location.SourceSpan);
    }

    // Removes the single [| |] marker pair and returns the source with the span it enclosed.
    private static (string Source, TextSpan Span) Unmark(string marked)
    {
        var start = marked.IndexOf("[|", StringComparison.Ordinal);
        var end = marked.IndexOf("|]", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "The source must mark one span with [| and |].");
        Assert.Equal(-1, marked.IndexOf("[|", start + 2, StringComparison.Ordinal));

        var source = new StringBuilder(marked.Length)
            .Append(marked, 0, start)
            .Append(marked, start + 2, end - start - 2)
            .Append(marked, end + 2, marked.Length - end - 2)
            .ToString();
        return (source, TextSpan.FromBounds(start, end - 2));
    }
}
