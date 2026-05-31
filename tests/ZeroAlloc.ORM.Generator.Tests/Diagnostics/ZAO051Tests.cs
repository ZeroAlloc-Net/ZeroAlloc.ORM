using System;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// v0.5 Phase D post-review Fix 2 — ZAO051 fires when [Materialize(Factory = "X")]
// is in effect and name-based matching reveals a factory parameter that does
// not match any candidate column name (case-insensitive). The candidate set
// comes from the underlying composite type's MultiArgCtor parameter names
// (PascalCased) — the documented contract is "rename the factory parameter,
// use SQL 'AS' alias, or align the SELECT column order".
//
// When candidate column names cannot be statically derived (the FlatRow
// positional path / composite-at-scalar path), ZAO051 does NOT fire and the
// generator falls back to positional matching — this is the documented
// fallback. The positive control test below covers the matching case (no
// ZAO051). The negative-fallback test covers the FlatRow positional path
// (factory param 'amountText' doesn't match 'Amount' / 'Currency' but
// ZAO051 is suppressed in the positional fallback context).
public class ZAO051Tests
{
    [Fact]
    public void Factory_param_name_mismatch_at_named_column_position_emits_ZAO051()
    {
        // Money is nested inside a FlatRow (OrderRow) — the FlatRow path uses
        // positional reads at the OUTER level, but the inner factory dispatch
        // can derive candidate names from Money's underlying ctor (Amount,
        // Currency) and apply name-based matching. The factory parameter
        // 'rawAmount' doesn't match either candidate -> ZAO051 fires.
        //
        // Wait — current FlatRow path uses useNamedColumns: false at the inner
        // level, so we model the failure via the DomainEntity (column-name-
        // keyed) path. Use a class with a single public ctor that nests Money.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            [Materialize(Factory = "FromStorage")]
            public readonly record struct Money(decimal Amount, string Currency)
            {
                // 'rawAmount' doesn't match either candidate name (Amount/Currency).
                public static Money FromStorage(string rawAmount, string currency)
                    => new Money(decimal.Parse(rawAmount, global::System.Globalization.CultureInfo.InvariantCulture), currency);
            }

            // DomainEntity shape — single public ctor on a non-record class.
            public sealed class Cart
            {
                public Cart(int id, Money total)
                {
                    Id = id;
                    Total = total;
                }
                public int Id { get; }
                public Money Total { get; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Carts WHERE Id = @id")]
                public partial Task<Cart?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO051", StringComparison.Ordinal));
    }

    [Fact]
    public void Factory_param_name_match_at_named_column_position_does_not_emit_ZAO051()
    {
        // Positive control — factory parameter names match the underlying ctor
        // parameter names (case-insensitive). No ZAO051.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            [Materialize(Factory = "FromStorage")]
            public readonly record struct Money(decimal Amount, string Currency)
            {
                public static Money FromStorage(decimal amount, string currency)
                    => new Money(amount, currency);
            }

            public sealed class Cart
            {
                public Cart(int id, Money total)
                {
                    Id = id;
                    Total = total;
                }
                public int Id { get; }
                public Money Total { get; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Carts WHERE Id = @id")]
                public partial Task<Cart?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO051", StringComparison.Ordinal));
    }

    [Fact]
    public void Positional_fallback_path_does_not_emit_ZAO051()
    {
        // Composite-at-scalar shape — no SQL column names are available at
        // classification time, so positional matching is the documented
        // fallback. Even though 'amountText' doesn't match 'Amount', ZAO051
        // must not fire because the name-based contract is not in effect.
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
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO051", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAO051_suppresses_ZAO022_and_ZAO040()
    {
        // Post-review Fix 4 — when a factory-specific diagnostic (ZAO043 /
        // ZAO044 / ZAO051) already fired, ZAO022 / ZAO040 must be suppressed
        // to avoid double-reporting the same defect.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

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
                public partial Task<Cart?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO051", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO022", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO040", StringComparison.Ordinal));
    }
}
