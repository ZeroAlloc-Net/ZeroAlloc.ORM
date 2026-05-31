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
    public void Sproc_tuple_with_one_matching_and_one_nonmatching_scalar_field_emits_ZAO062()
    {
        // Author intent ambiguity: `Total` (scalar int) could be a result-set
        // column or a typo'd output parameter. The classifier picks the
        // result-column interpretation (no matching C# parameter) and ZAO062
        // surfaces the ambiguity with the field name in the message so the
        // adopter can rename if the intent was output.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InsertOrder")]
                public partial Task<(int NewOrderId, int Total)> InsertAsync(
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
        // Message must name the non-matching field so the adopter can locate it
        // — a feature-gated grep in the assertion would mask renames.
        Assert.Contains("Total", zao062[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
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
