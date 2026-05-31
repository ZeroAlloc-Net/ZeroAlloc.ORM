using System.Linq;
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.4 Phase D.3 — [StoredProcedure] returning a multi-result-set tuple.
// Verifies that the existing v0.3 MultiResultSet emit picks up the sproc
// procedure-name + CommandType.StoredProcedure flip via the
// BuildCommandTextAssignment helper. Stored procedures default to
// BatchMode.Never (per StoredProcedureAttribute) so the strategy resolves to
// JoinedStatementsOnly emit — the procedure call is a single DbCommand whose
// reader walks multiple result sets via NextResultAsync.
public class StoredProcedureMultiResultTests
{
    [Fact]
    public Task StoredProcedure_multi_result_tuple_emits_single_command_with_NextResult()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);
            public sealed record OrderLineRow(int OrderId, int Sku, int Quantity);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_GetOrderWithLines")]
                public partial Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?> GetOrderWithLinesAsync(
                    int id, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void StoredProcedure_multi_result_does_not_emit_ZAO032_or_ZAO033()
    {
        // ZAO032/ZAO033 check tuple-arity vs SQL-statement-count. Stored procedures
        // carry empty SQL (statementCount == 0) but produce N result sets on the
        // server side; the diagnostics must be suppressed for the sproc path so the
        // legitimate multi-result-set sproc doesn't get rejected at compile time.
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);
            public sealed record OrderLineRow(int OrderId, int Sku, int Quantity);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_GetOrderWithLines")]
                public partial Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)?> GetOrderWithLinesAsync(
                    int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        Assert.DoesNotContain(diagnostics.AsEnumerable(),
            d => string.Equals(d.Id, "ZAO032", System.StringComparison.Ordinal)
                || string.Equals(d.Id, "ZAO033", System.StringComparison.Ordinal));
    }
}
