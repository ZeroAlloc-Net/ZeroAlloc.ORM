using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

public class ZAO001Tests
{
    [Fact]
    public void Method_without_partial_emits_ZAO001()
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
                public Task<int> GetOneAsync(CancellationToken ct) => throw null!;
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO001", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Method_with_partial_modifier_does_not_emit_ZAO001()
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

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO001", System.StringComparison.Ordinal));
    }
}
