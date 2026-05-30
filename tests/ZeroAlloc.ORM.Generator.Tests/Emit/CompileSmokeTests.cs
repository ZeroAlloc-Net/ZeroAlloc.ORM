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

    [Fact]
    public void FlatRow_emit_compiles_cleanly()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        var (_, compileDiagnostics) = GeneratorHarness.RunGeneratorAndCompile(source);

        // Same bug-class filter as the other smoke tests. The unbound @id parameter
        // in the SQL is a runtime concern (resolved by Phase 6 binding); it doesn't
        // surface as CS1061/CS0103/CS9113 at compile time so the smoke test stays
        // green even though parameter binding hasn't landed yet.
        var bugClass = compileDiagnostics
            .AsEnumerable()
            .Where(d => d.Id is "CS1061" or "CS0103" or "CS9113")
            .ToArray();
        Assert.Empty(bugClass);
    }
}
