using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

public class ZAO008Tests
{
    [Fact]
    public void Multi_statement_sql_with_single_result_return_emits_ZAO008()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1; SELECT 2")]
                public partial Task<int> GetOneAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO008", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Multi_statement_sql_with_tuple_return_does_not_emit_ZAO008()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1; SELECT 2")]
                public partial Task<(int, List<int>)> GetTwoAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO008", System.StringComparison.Ordinal));
    }
}
