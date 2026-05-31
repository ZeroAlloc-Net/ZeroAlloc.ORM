using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.4 Phase C.1 — when [Command(Kind = Identity)] is paired with a return type
// that doesn't reduce to a non-nullable int/long/Guid scalar (Task<List<int>>,
// Task<int?>, etc.) the classification falls through to EmitShape.Unknown.
// Identity has no nullable variant by design (the SQL contract requires the
// RETURNING / SCOPE_IDENTITY() clause to produce a non-null value); the
// generator surfaces the gap as a compile-time ZAO002 — same descriptor +
// pattern as Phase B's Scalar branch.
public class CommandIdentityDiagnosticsTests
{
    [Fact]
    public void Identity_with_unsupported_container_return_type_emits_ZAO002()
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
                // Task<List<int>> is not an identity-shaped return; Kind=Identity
                // requires a non-nullable int/long/Guid (or VO wrapping one). The
                // generator must surface ZAO002 at build time.
                [Command("INSERT INTO Orders (...) VALUES (...) RETURNING Id", Kind = CommandKind.Identity)]
                public partial Task<List<int>> InsertOrdersAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d =>
            string.Equals(d.Id, "ZAO002", System.StringComparison.Ordinal)
            && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
                .Contains("InsertOrdersAsync", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Identity_with_nullable_int_return_type_emits_ZAO002()
    {
        // Identity is NEVER nullable — the design contract says the SQL must
        // produce a non-null value. Task<int?> is therefore unsupported on
        // Kind=Identity and falls through to ZAO002.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (...) VALUES (...) RETURNING Id", Kind = CommandKind.Identity)]
                public partial Task<int?> InsertNullableAsync(CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;

        Assert.Contains(diagnostics, d =>
            string.Equals(d.Id, "ZAO002", System.StringComparison.Ordinal)
            && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
                .Contains("InsertNullableAsync", System.StringComparison.Ordinal));
    }
}
