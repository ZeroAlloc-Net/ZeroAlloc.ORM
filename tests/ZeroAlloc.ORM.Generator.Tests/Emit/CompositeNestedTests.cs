using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.5 Phase A.3 — composite materialization nested inside an outer FlatRow or
// DomainEntity row. The outer ctor arity may be smaller than the flattened
// column count; the column-index cursor advances across the composite's inner
// reads correctly.
//
// `Task<List<OrderRow>>` is NOT covered here — `Task<List<T>>` is not a
// supported top-level shape in the current generator surface (`Task<List<T>>`
// is rejected via ZAO022 today; tuple-of-list shapes via MultiResultSet are
// the only list-shaped path). Streaming (`IAsyncEnumerable<OrderRow>`) is the
// supported "yield rows" surface; that test lives in StreamingEmitTests.
// Adding `Task<List<T>>` as a standalone top-level shape is a separate feature
// and is left out of v0.5 Phase A by design.
public class CompositeNestedTests
{
    [Fact]
    public Task FlatRow_with_nested_composite_emits_nested_constructor()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);
            public sealed record OrderRow(int Id, Money Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task DomainEntity_with_nested_composite_emits_nested_constructor()
    {
        // Class with a single public ctor — DomainEntity shape. The composite's
        // inner reads route through GetOrdinal(<name>) so column order in the
        // SELECT clause is not load-bearing.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public readonly record struct Money(decimal Amount, string Currency);

            public class OrderEntity
            {
                public int Id { get; }
                public Money Total { get; }
                public OrderEntity(int id, Money total) { Id = id; Total = total; }
            }

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, Amount, Currency FROM Orders WHERE Id = @id")]
                public partial Task<OrderEntity?> GetByIdAsync(int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
