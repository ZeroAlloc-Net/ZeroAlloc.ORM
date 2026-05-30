using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

public class DomainEntityMaterializationTests
{
    // Plain class with a single multi-arg public ctor. Detection routes through
    // EmitShape.DomainEntity so the emit binds each ctor argument via
    // `__reader.GetOrdinal("ColumnName")` instead of a positional index.
    [Fact]
    public Task DomainEntity_class_emits_GetOrdinal_keyed_materialization()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed class Order
            {
                public int Id { get; }
                public int CustomerId { get; }
                public decimal Total { get; }
                public Order(int id, int customerId, decimal total) =>
                    (Id, CustomerId, Total) = (id, customerId, total);
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
                public partial Task<Order?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
