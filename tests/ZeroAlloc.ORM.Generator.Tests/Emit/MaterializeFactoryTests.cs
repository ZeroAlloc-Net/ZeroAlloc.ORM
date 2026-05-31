using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.5 Phase D.2 — [Materialize(Factory)] emit. Snapshots lock the
// exact factory-invocation source at every supported position:
//
//   * Scalar return Task<Money> + factory on the type.
//   * Method-level [return: Materialize(Factory)] override.
//   * Nested in a FlatRow row.
//   * Nested in a DomainEntity row (column-name keyed reads).
//
// The factory's parameter list — not the composite's underlying ctor —
// drives the inner-column shape. The canonical Sqlite case carries a
// `string` first param so the emit produces `GetString(0)` even though
// `Money.Amount` is `decimal`.
public class MaterializeFactoryTests
{
    [Fact]
    public Task Factory_on_type_at_scalar_return_emits_factory_call()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            [Materialize(Factory = "FromStorage")]
            public readonly record struct Money(decimal Amount, string Currency)
            {
                public static Money FromStorage(string amountText, string currency)
                    => new Money(decimal.Parse(amountText, global::System.Globalization.CultureInfo.InvariantCulture), currency);
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<Money> GetTotalAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Return_level_factory_annotation_overrides_type_default()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency)
            {
                public static Money FromStorage(string amountText, string currency)
                    => new Money(decimal.Parse(amountText, global::System.Globalization.CultureInfo.InvariantCulture), currency);
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [return: Materialize(Factory = "FromStorage")]
                [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<Money> GetTotalAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Factory_on_nested_composite_in_flat_row_emits_factory_call()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            [Materialize(Factory = "FromStorage")]
            public readonly record struct Money(decimal Amount, string Currency)
            {
                public static Money FromStorage(string amountText, string currency)
                    => new Money(decimal.Parse(amountText, global::System.Globalization.CultureInfo.InvariantCulture), currency);
            }

            public sealed record OrderRow(int Id, Money Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Factory_on_nullable_scalar_emits_all_or_nothing_with_factory_call()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            [Materialize(Factory = "FromStorage")]
            public readonly record struct Money(decimal Amount, string Currency)
            {
                public static Money FromStorage(string amountText, string currency)
                    => new Money(decimal.Parse(amountText, global::System.Globalization.CultureInfo.InvariantCulture), currency);
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<Money?> GetTotalAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
