using System.Linq;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// v1.0 Phase C (v0.4-CLN5) — ZAO064 fires when a `[StoredProcedure(...)]`
// method explicitly sets `Batch` to a value other than `BatchMode.Never`.
// Stored procedures encapsulate their own batching semantics server-side; the
// emit always treats the procedure call as a single DbCommand. The Batch
// value is silently ignored — ZAO064 surfaces the no-op at compile time.
//
// Coverage:
//   * Sproc with explicit Batch = Always — fires ZAO064.
//   * Sproc with explicit Batch = Auto — fires ZAO064 (Auto != Never).
//   * Sproc with explicit Batch = Never — does NOT fire (matches default).
//   * Sproc without Batch named arg — does NOT fire (attribute default Never).
//   * [Query] with Batch = Always — does NOT fire (sproc-only diagnostic).
public class ZAO064Tests
{
    [Fact]
    public void Sproc_with_explicit_batch_always_emits_ZAO064()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_GetOrder", Batch = BatchMode.Always)]
                public partial Task<OrderRow?> GetOrderAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        var zao064 = diagnostics
            .AsEnumerable()
            .Where(d => string.Equals(d.Id, "ZAO064", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Single(zao064);
        var message = zao064[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("Always", message, System.StringComparison.Ordinal);
        Assert.Contains("GetOrderAsync", message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Sproc_with_explicit_batch_auto_emits_ZAO064()
    {
        // Auto is also != Never; ZAO064 fires because the explicit value
        // diverges from the sproc default (and from what the emit honours).
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_GetOrder", Batch = BatchMode.Auto)]
                public partial Task<OrderRow?> GetOrderAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        var zao064 = diagnostics
            .AsEnumerable()
            .Where(d => string.Equals(d.Id, "ZAO064", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Single(zao064);
    }

    [Fact]
    public void Sproc_with_explicit_batch_never_does_not_emit_ZAO064()
    {
        // Explicit `Never` matches the sproc default — semantically a no-op
        // but the adopter's intent is now visible in source. ZAO064 must
        // not fire.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_GetOrder", Batch = BatchMode.Never)]
                public partial Task<OrderRow?> GetOrderAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO064", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Sproc_without_batch_arg_does_not_emit_ZAO064()
    {
        // The attribute default is `Never`; omitting the named arg is the
        // canonical sproc shape and must not fire ZAO064.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_GetOrder")]
                public partial Task<OrderRow?> GetOrderAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO064", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Query_with_explicit_batch_always_does_not_emit_ZAO064()
    {
        // ZAO064 is sproc-only — `[Query]` honours `Batch` at emit time
        // via ResolveBatchStrategy. The non-default value is meaningful
        // there, so no diagnostic fires.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId);
            public sealed record OrderLineRow(int OrderId, int Sku);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1; SELECT 2", Batch = BatchMode.Always)]
                public partial Task<(OrderRow, OrderLineRow)> GetAsync(int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO064", System.StringComparison.Ordinal));
    }
}
