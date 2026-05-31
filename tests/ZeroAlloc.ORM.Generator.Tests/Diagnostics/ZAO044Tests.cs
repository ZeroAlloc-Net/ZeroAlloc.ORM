using System;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// v0.5 Phase D post-review Fix 3 — ZAO044 fires when [Materialize(Factory = "X")]
// resolves to MULTIPLE static overloads with the same name on the target type.
// Overload selection by signature is non-deterministic and the generator
// refuses to silently pick the first one. Adopter fixes by reducing to a
// single static overload or by using a distinct factory name.
public class ZAO044Tests
{
    [Fact]
    public void Two_static_factory_overloads_emit_ZAO044()
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

                // Second static overload of the same name — ZAO044 fires.
                public static Money FromStorage(decimal amount, string currency)
                    => new Money(amount, currency);
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<Money> GetTotalAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO044", StringComparison.Ordinal));
        // Fix 4 — ZAO022 / ZAO040 suppressed when a factory diagnostic fired.
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO022", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO040", StringComparison.Ordinal));
    }

    [Fact]
    public void Single_static_factory_does_not_emit_ZAO044()
    {
        // Positive control — a single static factory of the named factory
        // resolves unambiguously. No ZAO044.
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

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO044", StringComparison.Ordinal));
    }
}
