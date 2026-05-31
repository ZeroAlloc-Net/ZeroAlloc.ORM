using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.5 Phase D.1 — [Materialize(Factory = "X")] detection. Per design Section 3
// (line 260, discovery-order rule #1), an explicit `[Materialize(Factory)]`
// annotation always wins over convention-discovery. The classifier MUST look up
// the named `static` method on the target type and route emit through a factory
// dispatch branch instead of the convention-discovered `new T(...)` constructor.
//
// The detection branch carries a sentinel comment so the test can pin the
// classifier dispatch without depending on the full emit body landing in D.2.
// The negative tests verify ZAO043 fires for missing / non-static factories.
public class MaterializeFactoryDetectionTests
{
    [Fact]
    public void Factory_annotation_on_type_reaches_factory_branch()
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
        var generated = GeneratorHarness.RunGenerator(source)
            .GeneratedTrees
            .Single(t => t.FilePath.EndsWith("Repo.g.cs", StringComparison.Ordinal))
            .GetText()
            .ToString();

        Assert.Contains("// EmitShape: composite-factory global::TestApp.Money.FromStorage (factory args: 2)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Method_level_return_factory_annotation_reaches_factory_branch()
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
        var generated = GeneratorHarness.RunGenerator(source)
            .GeneratedTrees
            .Single(t => t.FilePath.EndsWith("Repo.g.cs", StringComparison.Ordinal))
            .GetText()
            .ToString();

        Assert.Contains("// EmitShape: composite-factory global::TestApp.Money.FromStorage (factory args: 2)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Factory_annotation_on_nested_composite_ctor_parameter_reaches_factory_branch()
    {
        // [Materialize(Factory)] on the inner Money type — the OUTER OrderRow is a
        // plain FlatRow whose composite ctor param (Money Total) uses the factory
        // dispatch for its three-argument construction. The sentinel below proves
        // the classifier reached the factory branch for the nested position.
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
        var generated = GeneratorHarness.RunGenerator(source)
            .GeneratedTrees
            .Single(t => t.FilePath.EndsWith("Repo.g.cs", StringComparison.Ordinal))
            .GetText()
            .ToString();

        Assert.Contains("// FactoryDispatch: global::TestApp.Money.FromStorage", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Factory_name_not_found_emits_ZAO043()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            [Materialize(Factory = "DoesNotExist")]
            public readonly record struct Money(decimal Amount, string Currency);

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
    public void Factory_name_not_found_suppresses_ZAO022_and_ZAO040()
    {
        // Post-review Fix 4 — when ZAO043 fires, ZAO022 / ZAO040 must NOT also
        // fire (they would double-report the same defect).
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            [Materialize(Factory = "DoesNotExist")]
            public readonly record struct Money(decimal Amount, string Currency);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<Money> GetTotalAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO043", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO022", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO040", StringComparison.Ordinal));
    }

    [Fact]
    public void Materialize_strategy_custom_without_factory_emits_ZAO043()
    {
        // Post-review Fix 8 — [Materialize(Strategy = Custom)] without a
        // Factory argument is a silent no-op today. Surface ZAO043 with a
        // tailored reason so the adopter sees the misconfiguration.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            [Materialize(Strategy = MaterializeStrategy.Custom)]
            public readonly record struct Money(decimal Amount, string Currency);

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
    public void Factory_method_is_instance_not_static_emits_ZAO043()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            [Materialize(Factory = "FromStorage")]
            public sealed record Money(decimal Amount, string Currency)
            {
                // Instance method (no `static`): must be rejected by ZAO043.
                public Money FromStorage(string amountText, string currency)
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

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO043", StringComparison.Ordinal));
    }
}
