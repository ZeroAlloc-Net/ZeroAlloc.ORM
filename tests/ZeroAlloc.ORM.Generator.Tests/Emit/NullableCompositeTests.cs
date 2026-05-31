using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.5 Phase C.1 — nullable composite materialization. The all-or-nothing
// contract (design Section 3.5, line 330):
//
//   * All composite columns DBNull  -> return null.
//   * Any (but not all) DBNull      -> throw ZeroAllocOrmMaterializationException
//                                       naming the mixed columns.
//   * Otherwise                     -> materialize normally.
//
// Three target shapes covered:
//
//   * Task<Money?> scalar — positional ordinals, all-or-nothing branch wraps
//     `return new Money(...)`. Mixed-null path throws.
//
//   * record OrderRow(int Id, Money? Total) — FlatRow with a nested nullable
//     composite. The outer FlatRow can't inline the all-or-nothing check inside
//     the ctor argument expression, so the emit hoists a local `__total`
//     evaluated before the `new OrderRow(...)` call (see EmitFlatRowWithHoist).
//
//   * DomainEntity with a nested nullable composite — same hoisted-local
//     pattern but inner reads use GetOrdinal(<name>) instead of positional
//     indices.
public class NullableCompositeTests
{
    [Fact]
    public Task Nullable_composite_scalar_emits_all_or_nothing_check()
    {
        // The source-level `#pragma warning disable ZAO050` suppresses the
        // ZAO050 diagnostic in this snapshot so it captures only the emit
        // shape; ZAO050 detection is covered separately in ZAO050Tests.
        var source = """
            #pragma warning disable ZAO050
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<Money?> GetTotalAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Nullable_composite_nested_in_flat_row_emits_hoisted_local()
    {
        var source = """
            #pragma warning disable ZAO050
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);
            public sealed record OrderRow(int Id, Money? Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Nullable_composite_nested_in_domain_entity_emits_hoisted_local()
    {
        var source = """
            #pragma warning disable ZAO050
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);

            public class OrderEntity
            {
                public int Id { get; }
                public Money? Total { get; }
                public OrderEntity(int id, Money? total) { Id = id; Total = total; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<OrderEntity?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Nullable_composite_parameter_emits_DBNull_branch()
    {
        // v0.5 Phase C.2 (Option A) — `Money? total` parameter unpacks into
        // an `if (@total is null) { all DBNull } else { @total.Value.X }`
        // pattern. Pins the wire-level shape so a regression in the
        // nullable-composite bind branch surfaces as snapshot churn.
        var source = """
            #pragma warning disable ZAO050
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET Amount = @total_Amount, Currency = @total_Currency WHERE Id = @id", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateAsync(int id, Money? total, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
