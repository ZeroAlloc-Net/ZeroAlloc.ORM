using System.Linq;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

// v1.0 Phase D (v0.4-TX) — ZAO080 fires when a `[Command]` (or `[Query]` /
// `[StoredProcedure]`) method declares more than one IAsyncDbTransaction
// parameter. The emit forwards the FIRST matching parameter to
// `__cmd.Transaction` and silently drops the rest; surfacing the multiplicity
// at compile time mirrors the ZAO006 precedent for CancellationToken.
//
// Coverage:
//   * Two IAsyncDbTransaction parameters — ZAO080 fires.
//   * One IAsyncDbTransaction parameter  — ZAO080 does NOT fire
//                                          (false-positive guard).
public class TransactionParameterDiagnosticsTests
{
    [Fact]
    public void ZAO080_fires_when_method_declares_multiple_IAsyncDbTransaction_parameters()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE X SET A = @a WHERE Id = @id", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateAsync(int id, int a, IAsyncDbTransaction tx1, IAsyncDbTransaction tx2, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAO080", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ZAO080_does_not_fire_when_method_declares_one_IAsyncDbTransaction_parameter()
    {
        // Guard against false-positive: count == 1 is the canonical shape and
        // must stay silent.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE X SET A = @a WHERE Id = @id", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateAsync(int id, int a, IAsyncDbTransaction tx, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAO080", System.StringComparison.Ordinal));
    }
}
