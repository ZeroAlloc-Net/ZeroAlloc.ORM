using System.Threading.Tasks;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

public class ParamNameOverrideTests
{
    [Fact]
    public void Param_name_attribute_overrides_csharp_name()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1 WHERE @orderId = 42")]
                public partial Task<int> SearchAsync([Param(Name = "@orderId")] int id, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }
}
