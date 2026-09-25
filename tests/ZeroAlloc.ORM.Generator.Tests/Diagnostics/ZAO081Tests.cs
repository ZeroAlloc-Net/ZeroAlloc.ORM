using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// Issue #238 — ZAO081 fires when a containing type ABOVE the repository type
// itself is not declared partial. ZAO004 (see ZAO004Tests) already covers the
// repository type; these tests cover the outer wrapper(s).
public class ZAO081Tests
{
    [Fact]
    public void NonPartial_outer_containing_type_emits_ZAO081()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public class Data
            {
                public partial class OrderRepository(IAsyncDbConnection connection)
                {
                    [Query("SELECT 1")]
                    public partial Task<int> GetOneAsync(CancellationToken ct);
                }
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO081", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Partial_outer_containing_type_does_not_emit_ZAO081()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public partial class Data
            {
                public partial class OrderRepository(IAsyncDbConnection connection)
                {
                    [Query("SELECT 1")]
                    public partial Task<int> GetOneAsync(CancellationToken ct);
                }
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO081", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Two_non_partial_containing_types_emit_ZAO081_for_each()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public class Outer
            {
                public class Inner
                {
                    public partial class OrderRepository(IAsyncDbConnection connection)
                    {
                        [Query("SELECT 1")]
                        public partial Task<int> GetOneAsync(CancellationToken ct);
                    }
                }
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        var zao081Count = 0;
        foreach (var d in diagnostics)
        {
            if (string.Equals(d.Id, "ZAO081", System.StringComparison.Ordinal))
                zao081Count++;
        }
        Assert.Equal(2, zao081Count);
    }

    [Fact]
    public void NonPartial_outer_containing_type_suppresses_emit()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public class Data
            {
                public partial class OrderRepository(IAsyncDbConnection connection)
                {
                    [Query("SELECT 1")]
                    public partial Task<int> GetOneAsync(CancellationToken ct);
                }
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);

        Assert.Empty(result.GeneratedTrees);
    }
}
