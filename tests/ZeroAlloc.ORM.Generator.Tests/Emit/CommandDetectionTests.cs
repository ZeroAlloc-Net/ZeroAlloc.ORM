using System.Linq;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.4 Phase A.1 — [Command] attribute scanner DETECTION / DIAGNOSTIC coverage.
// This file's scope is intentionally narrow: it verifies the diagnostic-pipeline
// behaviour for [Command]-attributed methods (cross-attribute ZAO005, plus any
// future detection-only assertions). Full emit-shape snapshots for [Command]
// live in CommandNonQueryTests.cs so the snapshot and detection concerns don't
// drift together.
public class CommandDetectionTests
{
    [Fact]
    public void Method_with_query_and_command_attributes_emits_ZAO005()
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
                [Command("INSERT INTO X VALUES (1)")]
                public partial Task<int> DoSomethingAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        // Assert.Single (not Assert.Contains) so a future regression that fires
        // ZAO005 twice — e.g. both pipelines surfacing the diagnostic before the
        // union deduper drops one — is caught here.
        Assert.Single(diagnostics.AsEnumerable(), d => string.Equals(d.Id, "ZAO005", System.StringComparison.Ordinal));
    }
}
