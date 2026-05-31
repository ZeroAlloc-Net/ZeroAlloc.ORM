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
