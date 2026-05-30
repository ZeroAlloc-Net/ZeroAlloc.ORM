using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

public class ZAO004Tests
{
    [Fact]
    public void Type_without_partial_emits_ZAO004()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial Task<int> GetOneAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO004", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Partial_type_does_not_emit_ZAO004()
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

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO004", System.StringComparison.Ordinal));
    }

    // Regression for the R8 type-scoped hoist: ZAO004 is a type-scoped diagnostic and
    // must fire EXACTLY ONCE per non-partial type even when multiple [Query] methods
    // share that type. Before R8, the diagnostic was derived from "Methods[0]" and
    // structurally couldn't multi-fire, but the new model stores it directly on
    // QueryRepositoryModel — pin the once-per-type behavior so it can't regress.
    [Fact]
    public void ZAO004_fires_exactly_once_per_non_partial_type_with_multiple_methods()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial Task<int> AAsync(CancellationToken ct);

                [Query("SELECT 2")]
                public partial Task<int> BAsync(CancellationToken ct);

                [Query("SELECT 3")]
                public partial Task<int> CAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        var zao004Count = 0;
        foreach (var d in diagnostics)
        {
            if (string.Equals(d.Id, "ZAO004", System.StringComparison.Ordinal))
                zao004Count++;
        }
        Assert.Equal(1, zao004Count);
    }
}
