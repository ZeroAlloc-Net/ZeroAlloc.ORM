using System;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// v0.5 Phase D — ZAO043 fires when [Materialize(Factory = "X")] cannot
// resolve a callable static factory by that name (missing, non-static,
// non-accessible, or other resolution failure). The positive case asserts
// the "method not found" path; the negative case proves a properly defined
// static factory resolves cleanly.
public class ZAO043Tests
{
    [Fact]
    public void Materialize_factory_with_missing_method_emits_ZAO043()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            [Materialize(Factory = "FromStorage")]
            public readonly record struct Money(decimal Amount, string Currency);
            // No static FromStorage method exists — ZAO043 fires.

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<Money> GetTotalAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO043", StringComparison.Ordinal));
    }

    [Fact]
    public void Materialize_factory_with_valid_static_method_does_not_emit_ZAO043()
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

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO043", StringComparison.Ordinal));
    }
}
