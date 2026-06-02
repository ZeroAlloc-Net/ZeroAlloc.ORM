using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// Issue #101 — declared accessibility on the partial-method declaration must be
// mirrored exactly on the emitted implementation, or the C# compiler raises
// CS8799 ("Both partial member declarations must have identical accessibility
// modifiers"). Before the fix, every emit hardcoded `public partial`, so any
// adopter declaring `private partial Task<T> ...` got a build error and had to
// expose the helper on the type's public surface.
//
// One snapshot per non-trivial accessibility keyword. Public is already covered
// by every other emit test in the suite — no need for a duplicate row here.
public class PartialMethodAccessibilityTests
{
    [Fact]
    public Task Private_partial_method_emits_private_in_implementation()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@cust, @total) RETURNING Id",
                         Kind = CommandKind.Identity)]
                private partial Task<int> InsertOrderAsync(int cust, decimal total, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Internal_partial_method_emits_internal_in_implementation()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT COUNT(*) FROM Orders")]
                internal partial Task<int> CountAsync(CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
