using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// ZAO050 (Warning) — fires when a method uses a nullable composite type. The
// all-or-nothing DBNull contract for composite-shaped nullable returns is a
// RUNTIME concern (Sqlite et al don't surface column nullability at compile
// time), so the diagnostic flags every position where the runtime check
// applies. Adopters who can guarantee NOT-NULL columns suppress via
// `#pragma warning disable ZAO050`.
//
// Coverage:
//   * Task<Money?> return (scalar composite) — fires ZAO050.
//   * record OrderRow(int Id, Money? Total) returning Task<OrderRow?> —
//     fires ZAO050 (the inner nullable composite is what triggers).
//   * Task<int> M(Money? total, CT ct) parameter — fires ZAO050 (write side).
//   * Task<Money> M() (non-nullable scalar composite) — does NOT fire.
//   * record OrderRow(int Id, Money Total) (non-nullable nested) — does NOT fire.
public class ZAO050Tests
{
    [Fact]
    public void Nullable_composite_return_emits_ZAO050()
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
                [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<Money?> GetTotalAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO050", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Nullable_composite_nested_in_flat_row_emits_ZAO050()
    {
        var source = """
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
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO050", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Two_nullable_composite_parameters_emit_ZAO050_per_parameter()
    {
        // v0.5-CLN1 (v1.0 Phase C) — parameter-side per-position firing. Two
        // `Money?` parameters on the same method must produce two ZAO050
        // diagnostics anchored at each parameter's location so each
        // `#pragma warning disable ZAO050` scope is locally narrow.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET SubAmount = @sub_Amount, SubCurrency = @sub_Currency, TotAmount = @tot_Amount, TotCurrency = @tot_Currency WHERE Id = @id", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateAsync(int id, Money? sub, Money? tot, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        var zao050 = diagnostics
            .AsEnumerable()
            .Where(d => string.Equals(d.Id, "ZAO050", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, zao050.Length);
    }

    [Fact]
    public void Nullable_composite_parameter_emits_ZAO050()
    {
        // v0.5 Phase C.2 (Option A) — the parameter side mirrors the read side:
        // a `Money? total` parameter unpacks into N DbParameters whose values
        // are DBNull when `total is null`. ZAO050 fires for the write
        // direction too because the runtime all-or-nothing decision is
        // still implicit ("send N DBNulls when the composite is null").
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
                public partial Task<int> UpdateAsync(int id, Money? total, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO050", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Flat_row_with_two_nullable_composite_fields_emits_ZAO050_per_position()
    {
        // v0.5-CLN1 (v1.0 Phase C) — ZAO050 must fire once per nullable-
        // composite POSITION, not once per method site. Two `Money?` fields
        // in the same FlatRow row -> two separate ZAO050 diagnostics so each
        // suppression decision is its own line in the source.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);
            public sealed record OrderRow(int Id, Money? Subtotal, Money? Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, SubAmount, SubCurrency, TotAmount, TotCurrency FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        var zao050 = diagnostics
            .AsEnumerable()
            .Where(d => string.Equals(d.Id, "ZAO050", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, zao050.Length);
    }

    [Fact]
    public void Non_nullable_composite_return_does_not_emit_ZAO050()
    {
        // Negative control — a non-nullable composite return has no runtime
        // all-or-nothing decision (the composite is always present). ZAO050
        // must not fire.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<Money> GetTotalAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO050", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Non_nullable_composite_nested_in_flat_row_does_not_emit_ZAO050()
    {
        // Negative control — composite nested in FlatRow without nullable
        // annotation. ZAO050 must not fire.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);
            public sealed record OrderRow(int Id, Money Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO050", System.StringComparison.Ordinal));
    }
}
