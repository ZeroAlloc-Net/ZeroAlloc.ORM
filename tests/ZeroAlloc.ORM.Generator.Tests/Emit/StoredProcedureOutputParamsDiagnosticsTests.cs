using System.Linq;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.4 Phase F.3 — ZAO062 (named-tuple field does not match any parameter).
//
// ZAO062 surfaces a warning when a [StoredProcedure]-annotated method returns
// a named tuple in which AT LEAST ONE field matches a C# parameter (signalling
// the output-params pattern is in use) AND at least one OTHER tuple field does
// NOT match any C# parameter. The non-matching field is treated as a result
// column by the classifier, which is a legitimate shape (multi-result-set +
// output param), but the warning catches the common misuse case where the
// author intended the field as an output parameter but mistyped the name and
// silently demoted it to a result column.
//
// Fired only on the SprocWithOutputParams shape — i.e. at least one tuple
// field matched a parameter. A pure MultiResultSet shape (zero matches) is
// NOT a candidate for ZAO062 because the output-params pattern isn't in play.
//
// Severity: Warning (the shape still emits; the diagnostic is a hint).
public class StoredProcedureOutputParamsDiagnosticsTests
{
    [Fact]
    public void Sproc_tuple_with_two_nonmatching_scalars_plus_one_match_emits_ZAO062_on_second()
    {
        // Author intent ambiguity: with two non-matching scalar fields plus a
        // matching parameter, ZAO062 fires on the SECOND non-matching field
        // (`Total`) — the first (`Status`) is treated as the conventional
        // result-row position per Heuristic 1 (Phase F review Fix 1). The
        // typo-detection win: a second mistyped output (`Tota1` etc.) still
        // surfaces here even though the canonical 1-result+output shape is
        // silent.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InsertOrder")]
                public partial Task<(int Status, int Total, int NewOrderId)> InsertAsync(
                    int customerId, int newOrderId, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        var zao062 = diagnostics
            .AsEnumerable()
            .Where(d => string.Equals(d.Id, "ZAO062", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Single(zao062);
        // Message must name the SECOND non-matching field, not the first
        // (skipped) one. `Total` is the warned field; `Status` is the silent
        // conventional result-row position.
        var message = zao062[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("Total", message, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Status", message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Sproc_tuple_with_all_matching_fields_does_not_emit_ZAO062()
    {
        // Output-only sub-case: every tuple field matches a C# parameter, so
        // there's nothing to warn about. Classifier picks SprocWithOutputParams
        // with zero result positions; ZAO062 must not fire.
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
        var diagnostics = result.Results[0].Diagnostics;
        var zao062 = diagnostics
            .AsEnumerable()
            .Where(d => string.Equals(d.Id, "ZAO062", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(zao062);
    }

    [Fact]
    public void Sproc_tuple_with_zero_matching_fields_does_not_emit_ZAO062()
    {
        // Pure MultiResultSet shape (zero tuple fields match a C# parameter).
        // The output-params pattern isn't in use here, so ZAO062 is not
        // applicable — the adopter clearly intends a multi-result-set sproc.
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
                public partial Task<(OrderRow Head, IReadOnlyList<OrderLineRow> Lines)> GetOrderWithLinesAsync(
                    int id, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        var zao062 = diagnostics
            .AsEnumerable()
            .Where(d => string.Equals(d.Id, "ZAO062", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(zao062);
    }

    [Fact]
    public void Sproc_tuple_ZAO062_location_anchors_at_offending_tuple_element_span()
    {
        // v0.4-CLN6 (v1.0 Phase C) — ZAO062 must anchor at the offending
        // tuple-element syntax span, NOT at the whole return-type span.
        // Stacking diagnostics on the same span confuses IDEs and degrades
        // the multi-typo case where two non-matching fields produce two
        // ZAO062 diagnostics that should land on distinct squiggles.
        //
        // The location's source span must be a strict sub-range of the
        // return-type span and must cover (at minimum) the offending tuple
        // field's syntax.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InsertOrder")]
                public partial Task<(int Status, int Total, int NewOrderId)> InsertAsync(
                    int customerId, int newOrderId, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        var zao062 = diagnostics
            .AsEnumerable()
            .Where(d => string.Equals(d.Id, "ZAO062", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Single(zao062);

        var diagSpan = zao062[0].Location.SourceSpan;
        Assert.True(diagSpan.Length > 0, "ZAO062 location must have a non-zero span.");

        // The full return type is `Task<(int Status, int Total, int NewOrderId)>`.
        // The diagnostic must NOT span the entire return type — that would be
        // the pre-CLN6 behaviour. We assert by checking the span is materially
        // shorter than the full tuple-syntax span.
        var diagSource = source.Substring(diagSpan.Start, diagSpan.Length);
        Assert.Contains("Total", diagSource, System.StringComparison.Ordinal);
        // The offending field is `int Total`; the span must NOT also cover
        // `NewOrderId` (the next field) — that would mean we're anchoring on
        // the whole tuple, not the element.
        Assert.DoesNotContain("NewOrderId", diagSource, System.StringComparison.Ordinal);
        // Likewise the span must not cover `Status` (the skipped first
        // non-matching field, conventional result-row position).
        Assert.DoesNotContain("Status", diagSource, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Sproc_tuple_ZAO062_two_typos_produce_two_diagnostics_at_distinct_spans()
    {
        // v0.4-CLN6 (v1.0 Phase C) — the multi-typo case: three non-matching
        // tuple fields produce two ZAO062 diagnostics (Heuristic 1 skips the
        // first), and the two diagnostics must land on DISTINCT source spans.
        // Pre-CLN6 both anchored at the whole return-type and IDE dedupe
        // collapsed them into one squiggle.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InsertOrder")]
                public partial Task<(int Status, int Total, int Subtotal, int NewOrderId)> InsertAsync(
                    int customerId, int newOrderId, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        var zao062 = diagnostics
            .AsEnumerable()
            .Where(d => string.Equals(d.Id, "ZAO062", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, zao062.Length);

        var span1 = zao062[0].Location.SourceSpan;
        var span2 = zao062[1].Location.SourceSpan;
        Assert.NotEqual(span1, span2);
    }

    [Fact]
    public void Sproc_tuple_with_query_attribute_does_not_emit_ZAO062()
    {
        // ZAO062 is gated on [StoredProcedure] — [Query] tuple returns stay on
        // the MultiResultSet path and a tuple field name happening to match
        // a C# parameter is coincidence (parameter binding is by name, not by
        // tuple-field rebinding). No output-params pattern, no ZAO062.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT 1; SELECT 2")]
                public partial Task<(int newOrderId, int Total)> GetAsync(
                    int newOrderId, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        var zao062 = diagnostics
            .AsEnumerable()
            .Where(d => string.Equals(d.Id, "ZAO062", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(zao062);
    }
}
