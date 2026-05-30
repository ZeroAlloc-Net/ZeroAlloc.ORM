using System.Threading.Tasks;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests;

public class AttributeScanTests
{
    [Fact]
    public Task Annotated_partial_method_produces_partial_file()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class OrderRepository(IAsyncDbConnection connection)
            {
                [Query("SELECT 1")]
                public partial Task<int> GetOneAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        return Verify(result);
    }
}
