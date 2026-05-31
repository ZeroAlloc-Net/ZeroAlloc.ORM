using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.4 Phase B code-review Fix 2 — when [Command(Kind = Scalar)] is paired with
// a return type that doesn't reduce to a scalar (Task<List<int>>, tuple, etc.)
// the classification falls through to EmitShape.Unknown. Previously this would
// fire the Phase A NotImplementedException runtime stub; the fix routes the
// gap through ZAO002 (existing "unsupported return type" descriptor) so the
// compile-time message names the bad shape before the consumer ever runs the
// code.
public class CommandScalarDiagnosticsTests
{
    [Fact]
    public void Scalar_with_unsupported_container_return_type_emits_ZAO002()
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
                // Task<List<int>> is not a scalar shape; Kind=Scalar requires a
                // primitive / VO / enum reducing to a single value. The
                // generator must surface ZAO002 at build time, not let the
                // runtime stub throw.
                [Command("SELECT Id FROM Orders", Kind = CommandKind.Scalar)]
                public partial Task<List<int>> GetIdsAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d =>
            string.Equals(d.Id, "ZAO002", System.StringComparison.Ordinal)
            && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
                .Contains("GetIdsAsync", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Scalar_with_unsupported_shape_skips_partial_implementation_emit()
    {
        // ZAO002 is severity Error; the hadError gate in ReportDiagnostics
        // skips the EmitRepository call so no partial implementation is added
        // for the offending method. Verify by walking the generated trees and
        // asserting GetIdsAsync does NOT appear as an emitted method body.
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("SELECT Id FROM Orders", Kind = CommandKind.Scalar)]
                public partial Task<List<int>> GetIdsAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var trees = result.Results[0].GeneratedSources;

        // EPS06-safe: explicit foreach avoids the hidden ImmutableArray<T> copy
        // LINQ would introduce by boxing the struct into IEnumerable<T>.
        var anyImplemented = false;
        foreach (var tree in trees)
        {
            var text = tree.SourceText.ToString();
            if (text.Contains("public partial async", System.StringComparison.Ordinal)
                && text.Contains("GetIdsAsync", System.StringComparison.Ordinal))
            {
                anyImplemented = true;
                break;
            }
        }

        Assert.False(anyImplemented, "ZAO002 (Error) must short-circuit Emit so no partial implementation is generated.");
    }
}
