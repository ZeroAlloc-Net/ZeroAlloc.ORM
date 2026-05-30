using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

public class EnumAsStringMaterializationTests
{
    // [StoreAsString] flips the enum's storage convention from int round-trip to
    // string round-trip — column materialization parses via Enum.Parse<T>, parameter
    // binding calls `.ToString()`. The AOT-trim caveat is documented inline in the
    // generator (Enum.Parse<T> carries RequiresUnreferencedCode but is safe for
    // closed enum types).
    [Fact]
    public Task StoreAsString_enum_round_trip_uses_Enum_Parse_and_ToString()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            [StoreAsString]
            public enum OrderStatus { Pending, Cancelled }

            public sealed record OrderRow(int Id, OrderStatus Status);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Status FROM Orders WHERE Status = @status LIMIT 1")]
                public partial Task<OrderRow?> SearchAsync(OrderStatus status, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
