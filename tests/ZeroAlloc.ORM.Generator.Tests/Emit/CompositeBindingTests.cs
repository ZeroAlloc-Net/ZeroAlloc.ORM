using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.5 Phase B.2 — composite parameter binding emit. Snapshots pin the
// `__p_{paramName}_{ctorArgName}` DbParameter blocks for the canonical
// shapes:
//
//   * Single composite parameter (`Money total`).
//   * Composite alongside a primitive (`int id, Money total`).
//   * Composite alongside a ValueObject (`Money total, OrderId orderId`).
//   * Composite with a ValueObject inner field (`MoneyWithOrderId outer`)
//     — the inner field's `.Value` unwrap mirrors the materialization side's
//     recursive convention layering.
public class CompositeBindingTests
{
    [Fact]
    public Task Single_composite_parameter_unpacks_into_two_parameters()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET Amount = @total_Amount, Currency = @total_Currency WHERE Id = 1", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateTotalAsync(Money total, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Composite_parameter_alongside_primitive_unpacks_correctly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET Amount = @total_Amount, Currency = @total_Currency WHERE Id = @id", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateAsync(int id, Money total, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Composite_parameter_alongside_value_object_unpacks_correctly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;
            using ZeroAlloc.ValueObjects;

            namespace TestApp;

            [ValueObject]
            public readonly partial struct OrderId
            {
                public int Value { get; }
                public OrderId(int v) { Value = v; }
                public static OrderId From(int value) => new(value);
            }

            public readonly record struct Money(decimal Amount, string Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET Amount = @total_Amount, Currency = @total_Currency WHERE Id = @orderId", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateAsync(Money total, OrderId orderId, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Composite_with_enum_inner_field_emits_cast_via_underlying()
    {
        // v0.5 Phase B code-review Fix 3 — the inner enum branch of
        // BuildCompositeFieldValueExpression was previously unexercised by
        // snapshot tests. Pin the `(int)@total.@Tier` cast emit so a regression
        // in either the inner convention resolution or the bind-side cast
        // surfaces immediately.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public enum Tier { Bronze, Silver, Gold }

            public readonly record struct Pricing(decimal Amount, Tier Tier);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET Amount = @total_Amount, Tier = @total_Tier WHERE Id = 1", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateTotalAsync(Pricing total, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Composite_with_string_enum_inner_field_emits_ToString()
    {
        // v0.5 Phase B code-review Fix 3 — the inner EnumAsString branch was
        // also unexercised. Pin the `@total.@Tier.ToString()` emit. Combined
        // with the cast test above, both enum branches of the composite-field
        // value expression are now covered.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            [StoreAsString]
            public enum Tier { Bronze, Silver, Gold }

            public readonly record struct Pricing(decimal Amount, Tier Tier);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET Amount = @total_Amount, Tier = @total_Tier WHERE Id = 1", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateTotalAsync(Pricing total, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Composite_with_nullable_string_inner_field_emits_DBNull_coalesce()
    {
        // v0.5 Phase B code-review Fix 4 — IsNullable branch of the composite
        // emit helper was previously unexercised. A nullable inner field
        // (`string? Currency`) must route through DBNull.Value the same way
        // primitive nullable parameters do — the `(object?){expr} ?? DBNull`
        // shape is shared with EmitParameterBinding.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string? Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET Amount = @total_Amount, Currency = @total_Currency WHERE Id = 1", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateTotalAsync(Money total, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Composite_with_batch_mode_emits_indexed_locals()
    {
        // v0.5 Phase B code-review Fix 5 — the batch path through
        // EmitBatchCommandParameterBinding short-circuits to the merged
        // composite emit helper, but no test exercised the cmdIndex suffix
        // behaviour. Pin the `_0` / `_1` local-suffix shape so a regression
        // in the helper's batch branch doesn't slip past — two BatchCommands
        // referencing the same composite parameter MUST produce distinct
        // locals (`__p_total_Amount_0` / `__p_total_Amount_1`) sharing the
        // same wire-level DbParameter name (`@total_Amount`).
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);
            public sealed record OrderRow(int Id, decimal Total);
            public sealed record OrderLineRow(string Sku, int Quantity);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Total FROM Orders WHERE Total >= @total_Amount AND Currency = @total_Currency; SELECT Sku, Quantity FROM OrderLines WHERE Total >= @total_Amount;", Batch = BatchMode.Always)]
                public partial Task<(OrderRow Head, List<OrderLineRow> Lines)?> GetMatchingAsync(Money total, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Composite_with_value_object_inner_field_unwraps_via_Value()
    {
        // Layered convention: the OUTER composite is unpacked into
        // `@outer_Amount` and `@outer_Currency`; the INNER `Currency` field
        // is a VO whose Value property unwrap must thread through the bind
        // expression — `@outer.Currency.Value`, not `@outer.Currency`.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;
            using ZeroAlloc.ValueObjects;

            namespace TestApp;

            [ValueObject]
            public readonly partial struct OrderId
            {
                public int Value { get; }
                public OrderId(int v) { Value = v; }
                public static OrderId From(int value) => new(value);
            }

            public readonly record struct MoneyWithOrderId(decimal Amount, OrderId Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET Amount = @outer_Amount, Currency = @outer_Currency WHERE Id = 1", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateAsync(MoneyWithOrderId outer, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
