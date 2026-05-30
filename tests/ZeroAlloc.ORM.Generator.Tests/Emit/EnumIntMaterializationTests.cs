using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

public class EnumIntMaterializationTests
{
    // Enums default to int round-trip — the column read is wrapped in an enum-type
    // cast (`(global::TestApp.OrderStatus)__reader.GetInt32(N)`) and the parameter
    // bind unwraps via an `(int)@status` cast. No factory method, no value property.
    [Fact]
    public Task Enum_column_and_parameter_round_trip_via_int_cast()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

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
