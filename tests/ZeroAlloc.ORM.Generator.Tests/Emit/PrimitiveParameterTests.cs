using System.Linq;
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

public class PrimitiveParameterTests
{
    [Fact]
    public Task Int_parameter_emits_binding()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1 WHERE @id = 42")]
                public partial Task<int> SearchAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void Primitive_parameter_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1 WHERE @id = 42")]
                public partial Task<int> SearchAsync(int id, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113")
            .ToArray();
        Assert.Empty(bugClass);
    }
}
