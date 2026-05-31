using System.Linq;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.4 Phase E.1 — [StoredProcedure] named-tuple output parameter DETECTION.
// Owns the classification-pipeline behaviour only: the new
// EmitShape.SprocWithOutputParams branch is reached when at least one tuple
// element's field name matches (case-insensitive) a C# parameter name on the
// method. Snapshot / emit coverage lives in StoredProcedureOutputParamsEmitTests
// (Phase E.2/E.3) so the detection cell can fail cleanly if the classifier
// regresses without dragging the emit snapshots through every change.
//
// Detection ladder:
//   * tuple field matches a C# param  -> output position (Direction.Output)
//   * tuple field has no match        -> result position (single row / scalar /
//                                          list, classified via the existing
//                                          MultiResultElement rules)
//   * at least one match              -> EmitShape.SprocWithOutputParams
//   * zero matches                    -> fall through to MultiResultSet (no
//                                          output params; existing Phase D path)
//
// Detection assertion strategy: each shape (output-only and result+output)
// emits a unique sentinel comment line so the test can grep for it. The Phase
// E.2/E.3 real emit replaces the stub body but keeps the sentinel marker for
// detection-test stability.
public class StoredProcedureOutputParamsDetectionTests
{
    private const string SentinelMarker = "// EmitShape.SprocWithOutputParams";

    [Fact]
    public void Sproc_tuple_with_matching_parameter_field_reaches_output_params_shape()
    {
        // Classifier must accept (OrderRow Result, int NewOrderId) with C#
        // parameter `newOrderId` as the SprocWithOutputParams shape — NOT the
        // existing MultiResultSet path (which would emit NextResultAsync for the
        // scalar element instead of binding it as an output parameter).
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InsertOrder")]
                public partial Task<(OrderRow Result, int NewOrderId)> InsertAsync(
                    int customerId, int newOrderId, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var generated = result.Results[0].GeneratedSources[0].SourceText.ToString();

        Assert.Contains(SentinelMarker, generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Sproc_tuple_field_name_matches_parameter_case_insensitively()
    {
        // C# parameter `newOrderId` (camelCase) matches tuple field `NewOrderId`
        // (PascalCase). The classifier must accept the case-insensitive pairing
        // because SQL parameter naming is case-insensitive on all major providers
        // and C# conventions disagree on case for the two slots. Same source as
        // the first test — included as a standalone case so a future regression
        // that hardcodes ordinal-compare gets a clear failing test.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InsertOrder")]
                public partial Task<(OrderRow Result, int NewOrderId)> InsertAsync(
                    int customerId, int newOrderId, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var generated = result.Results[0].GeneratedSources[0].SourceText.ToString();

        Assert.Contains(SentinelMarker, generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Sproc_tuple_with_no_matching_parameter_falls_through_to_multi_result_set()
    {
        // Negative case: the tuple has element names `Head` / `Lines` neither of
        // which match a C# parameter. The classifier MUST fall through to the
        // existing MultiResultSet path (Phase D.3) — no SprocWithOutputParams
        // shape. Asserting the sentinel is ABSENT proves the classifier didn't
        // mistakenly grab this tuple.
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
        var generated = result.Results[0].GeneratedSources[0].SourceText.ToString();

        Assert.DoesNotContain(SentinelMarker, generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Sproc_tuple_with_all_fields_matching_parameters_reaches_output_params_shape()
    {
        // Output-only case (every tuple field matches a C# parameter, no result
        // set). Classifier must still pick the SprocWithOutputParams shape;
        // Task E.3 detects the "output-only" sub-case at emit time to swap
        // ExecuteReaderAsync for ExecuteNonQueryAsync.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InsertOrder")]
                public partial Task<(int NewOrderId, int Status)> InsertAsync(
                    int customerId, int newOrderId, int status, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var generated = result.Results[0].GeneratedSources[0].SourceText.ToString();

        Assert.Contains(SentinelMarker, generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Sproc_tuple_output_params_emits_no_unexpected_diagnostics()
    {
        // Sanity check: a clean output-params shape doesn't fire ZAO022/ZAO040/
        // ZAO005 or any other ZAO* warnings. Diagnostics for the new shape
        // (ZAO062 — unmatched tuple field) are Phase F territory; here we only
        // verify the classification is clean.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InsertOrder")]
                public partial Task<(OrderRow Result, int NewOrderId)> InsertAsync(
                    int customerId, int newOrderId, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        var zao = diagnostics
            .AsEnumerable()
            .Where(d => d.Id.StartsWith("ZAO", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(zao);
    }
}
