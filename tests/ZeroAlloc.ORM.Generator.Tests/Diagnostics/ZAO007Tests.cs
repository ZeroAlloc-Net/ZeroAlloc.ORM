using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

public class ZAO007Tests
{
    [Fact]
    public void IAsyncEnumerable_without_EnumeratorCancellation_emits_ZAO007()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial IAsyncEnumerable<int> StreamAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO007", System.StringComparison.Ordinal));
    }

    [Fact]
    public void IAsyncEnumerable_with_no_CancellationToken_emits_ZAO007()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial IAsyncEnumerable<int> StreamAsync();
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        Assert.Contains(result.Results[0].Diagnostics, d => string.Equals(d.Id, "ZAO007", System.StringComparison.Ordinal));
    }

    [Fact]
    public void IAsyncEnumerable_with_EnumeratorCancellation_does_not_emit_ZAO007()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial IAsyncEnumerable<int> StreamAsync([EnumeratorCancellation] CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO007", System.StringComparison.Ordinal));
    }
}
