using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

public class ZAO002Tests
{
    [Fact]
    public void Method_with_unsupported_return_type_emits_ZAO002()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial int GetOne(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO002", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Method_with_TaskOfT_return_type_does_not_emit_ZAO002()
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

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO002", System.StringComparison.Ordinal));
    }
}
