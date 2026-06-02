using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// ZAO022 informs adopters when the return-type passes the v0.1 surface check (Task<T>,
// ValueTask<T>, IAsyncEnumerable<T>) but the generator's emit pipeline doesn't yet have
// a template for that specific shape — e.g. Task<HashSet<TRow>>. Without this hint the
// adopter sees a downstream CS8795 with no ZA-specific guidance.
//
// Note: List<T>, IList<T>, IReadOnlyList<T> at the top level are SUPPORTED shapes
// (issue #102 + v1.3.1 follow-up). HashSet<T> is genuinely unsupported and stays
// useful as the ZAO022 canary shape.
public class ZAO022Tests
{
    [Fact]
    public void Unknown_shape_Task_of_HashSet_emits_ZAO022()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT id FROM orders")]
                public partial Task<HashSet<OrderRow>> GetOrdersAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO022", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Supported_ScalarInt_shape_does_not_emit_ZAO022()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial Task<int> GetOneAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO022", System.StringComparison.Ordinal));
    }
}
