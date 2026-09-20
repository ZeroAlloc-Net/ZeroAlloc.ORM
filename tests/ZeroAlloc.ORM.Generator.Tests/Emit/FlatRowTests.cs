using System.Threading.Tasks;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

public class FlatRowTests
{
    [Fact]
    public void Positional_record_emits_FlatRow_materialization()
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
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }
}
