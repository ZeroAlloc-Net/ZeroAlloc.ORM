using System.Linq;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.4 Phase D.1 — [StoredProcedure] attribute scanner DETECTION / DIAGNOSTIC coverage.
// Mirrors the Phase A CommandDetectionTests shape: this file owns only the
// diagnostic-pipeline behaviour for [StoredProcedure]-attributed methods (cross-
// attribute ZAO005 exclusivity with [Query] and [Command]). Full emit-shape
// snapshots live in StoredProcedureEmitTests (Phase D.2) and
// StoredProcedureMultiResultTests (Phase D.3) so snapshot / detection concerns
// don't drift together.
public class StoredProcedureDetectionTests
{
    [Fact]
    public void Method_with_storedprocedure_attribute_does_not_emit_unexpected_diagnostics()
    {
        // Sanity check: a clean [StoredProcedure] method routes through the scanner
        // without firing ZAO001/ZAO002/ZAO005/etc. — i.e. the third pipeline picks
        // it up and the union dedup correctly admits the method into the emit graph.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_GetOne")]
                public partial Task<int> GetOneAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        // No ZAO* diagnostics expected — the method is partial, return type is
        // supported, single CT param, no attribute conflicts.
        var zao = diagnostics
            .AsEnumerable()
            .Where(d => d.Id.StartsWith("ZAO", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(zao);
    }

    [Fact]
    public void Method_with_query_and_storedprocedure_attributes_emits_ZAO005()
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
                [StoredProcedure("usp_X")]
                public partial Task<int> DoSomethingAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        // Assert.Single matches the Phase A precedent: a future regression that
        // fires ZAO005 twice (e.g. union dedup mis-step) is caught here.
        Assert.Single(diagnostics.AsEnumerable(), d => string.Equals(d.Id, "ZAO005", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Method_with_command_and_storedprocedure_attributes_emits_ZAO005()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO X VALUES (1)")]
                [StoredProcedure("usp_X")]
                public partial Task<int> DoSomethingAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Single(diagnostics.AsEnumerable(), d => string.Equals(d.Id, "ZAO005", System.StringComparison.Ordinal));
    }
}
