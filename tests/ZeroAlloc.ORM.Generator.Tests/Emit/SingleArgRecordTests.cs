using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

public class SingleArgRecordTests
{
    // Plain single-arg-ctor record (NO [ValueObject] attribute) — ConventionDiscovery
    // routes this through SingleArgCtor, so materialization uses `new T(...)` and
    // parameter binding still unwraps via the primary-ctor-synthesized Value property.
    [Fact]
    public Task Single_arg_record_emits_ctor_and_value_unwrap()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly partial record struct OrderId(int Value);

            public sealed record OrderRow(OrderId Id, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Total FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetByIdAsync(OrderId id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
