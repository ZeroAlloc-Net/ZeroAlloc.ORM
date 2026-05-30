using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

public class ZAO003Tests
{
    [Fact]
    public void Type_without_IAsyncDbConnection_emits_ZAO003()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo
            {
                [Query("SELECT 1")]
                public partial Task<int> GetOneAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO003", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Non_partial_type_with_no_connection_emits_ZAO004_only_not_ZAO003()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            // Non-partial class with no connection — both ZAO003 + ZAO004 would fire
            // pre-fix. Post-fix: only ZAO004 fires.
            public sealed class Repo
            {
                [Query("SELECT 1")]
                public partial Task<int> GetAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO004", System.StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO003", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Type_with_IAsyncDbConnection_does_not_emit_ZAO003()
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

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO003", System.StringComparison.Ordinal));
    }
}
