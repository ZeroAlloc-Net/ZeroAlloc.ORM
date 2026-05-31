using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.5 Phase E.2 — locked-in regression snapshots for composite × value-object
// combinations not already pinned by Phase A/B/C snapshots.
//
// The Phase A/B tests already cover:
//
//   * `Money(decimal, OrderId Currency)` materialization — VO at the INNER
//     ctor position (CompositeEmitTests.Composite_scalar_with_value_object_inner_unwraps_via_factory).
//   * `Money(decimal, OrderId Currency)` binding — VO at the INNER bind
//     position (CompositeBindingTests.Composite_with_value_object_inner_field_unwraps_via_Value).
//   * `Update(Money total, OrderId orderId)` — VO and composite ALONGSIDE
//     each other as separate method parameters (CompositeBindingTests.Composite_parameter_alongside_value_object_unpacks_correctly).
//
// What was NOT pinned: VO at the OUTER (row-level) position alongside a
// nested composite — `record OrderRow(OrderId Id, Money Total)` and its
// nullable-composite cousin. Both are valid Phase A + Phase C combinations
// and the snapshots below pin them so a future refactor of the layered
// convention resolution surfaces a diff churn instead of silent shape drift.
public class CompositeVOComboTests
{
    [Fact]
    public Task FlatRow_with_value_object_outer_and_composite_nested_emits_layered_reads()
    {
        // VO at the OUTER ctor position (OrderId Id) + composite NESTED
        // (Money Total). The FlatRow path threads through:
        //   * `OrderId.From(__reader.GetInt32(0))` for column 0,
        //   * `new Money(__reader.GetDecimal(1), __reader.GetString(2))` for columns 1-2.
        // Pinning both layered conventions in a single snapshot ensures the
        // composite-nested column-index cursor and the VO factory dispatch
        // continue to compose without interference.
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
            public sealed record OrderRow(OrderId Id, Money Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task FlatRow_with_value_object_outer_and_nullable_composite_nested_emits_all_or_nothing()
    {
        // Same layered convention as above plus a nullable composite at the
        // nested position (`Money? Total`). The hoisted-local + all-or-nothing
        // emit branch from Phase C must compose with the VO outer position —
        // ZAO050 fires once for the nullable composite; the VO column 0 read
        // stays straight. This pins the no-cross-talk contract.
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
            public sealed record OrderRow(OrderId Id, Money? Total);

            #pragma warning disable ZAO050
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
            }
            #pragma warning restore ZAO050
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
