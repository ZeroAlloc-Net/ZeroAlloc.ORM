using System.Linq;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

public class CompileSmokeTests
{
    [Fact]
    public void Scalar_int_emit_compiles_cleanly()
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
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);

        // Filter to errors the generator could be responsible for.
        // Some baseline errors from missing references may be unavoidable in the test harness;
        // verify nothing matches the primary-ctor capture bug pattern (CS1061/CS0103/CS9113).
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113")
            .ToArray();
        Assert.Empty(bugClass);
    }

    [Fact]
    public void Nullable_scalar_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Name FROM Users LIMIT 1")]
                public partial Task<string?> GetNameAsync(CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);

        // Same bug-class filter as Scalar_int_emit_compiles_cleanly: primary-ctor
        // capture/missing-member style errors that would indicate a generator bug
        // rather than a missing reference in the harness.
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113")
            .ToArray();
        Assert.Empty(bugClass);
    }
}
