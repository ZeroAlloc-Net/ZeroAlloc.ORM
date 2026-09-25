using System.Threading.Tasks;
using Xunit;

using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v0.4 Phase E.2/E.3 — [StoredProcedure] named-tuple output parameter EMIT.
// Snapshot coverage for the SprocWithOutputParams shape:
//
//   * 1 result-row + 1 int output param        — the canonical insert-and-return-id
//                                                  shape with a result set.
//   * 1 result-row + 2 output params (int+Guid) — exercises multi-output ordering and
//                                                  type-funnel via BuildScalarConvertExpression
//                                                  for both primitive shapes.
//   * 1 result-row + 1 value-object output param — exercises convention wrapping
//                                                   (`new OrderId(...)`) over the
//                                                   parameter readback.
//   * output-only (E.3)                         — every tuple field matches a parameter;
//                                                  emit swaps ExecuteReaderAsync for
//                                                  ExecuteNonQueryAsync.
//
// The reader-drain block (while-ReadAsync + while-NextResultAsync) is critical:
// SqlClient / Npgsql / Microsoft.Data.Sqlite only populate Parameter.Value after
// the reader closes. The drain happens INSIDE the scoped `await using` so the
// reader is disposed before the parameter readback runs.
public class StoredProcedureOutputParamsEmitTests
{
    [Fact]
    public void SprocWithOutputParams_result_row_plus_int_output_emits_drain_and_readback()
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
                [StoredProcedure("usp_InsertOrder")]
                public partial Task<(OrderRow Result, int NewOrderId)> InsertAsync(
                    int customerId, int newOrderId, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void SprocWithOutputParams_result_row_plus_two_outputs_emits_both_readbacks()
    {
        // int + Guid output params on top of a result row. Verifies the per-output
        // readback loop preserves tuple-position ordering across mixed primitive
        // types and that the Convert.ToXxx funnel kicks in for int while the
        // direct-cast fallback handles Guid (no Convert.ToGuid in BCL).
        var source = """
            using System;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InsertOrderWithTrace")]
                public partial Task<(OrderRow Result, int NewOrderId, Guid TraceId)> InsertAsync(
                    int customerId, int newOrderId, Guid traceId, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void SprocWithOutputParams_output_only_emits_ExecuteNonQuery()
    {
        // Task E.3 — every tuple field matches a C# parameter; the procedure has
        // no result set. Emit swaps ExecuteReaderAsync for ExecuteNonQueryAsync;
        // no reader scope, no drain loop, just the parameter readback after the
        // command completes. ResultElements.Length == 0 drives the branch in
        // EmitSprocWithOutputParams.
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
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void SprocWithOutputParams_result_row_plus_value_object_output_wraps_factory()
    {
        // Value-object output: the int read back from the parameter is wrapped in
        // the record's positional ctor before being assigned to the tuple slot.
        // Confirms the convention-discovery funnel applies to output positions
        // the same way it does to scalar materialization.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);
            public sealed record OrderId(int Value);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InsertOrder")]
                public partial Task<(OrderRow Result, OrderId NewOrderId)> InsertAsync(
                    int customerId, OrderId newOrderId, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void SprocWithOutputParams_nullable_int_output_emits_DBNull_guard()
    {
        // Phase E review Fix 1 — a nullable output element (`int?
        // OptionalCount`) must emit a DBNull guard in the readback expression.
        // Without the guard the direct Convert.ToInt32 call (or the cast
        // fallback for Guid etc.) would throw InvalidCastException when the
        // procedure leaves the output parameter at DBNull. The `is DBNull ?
        // null : ...` ternary keeps the contract symmetric with scalar
        // materialization's null tolerance. Non-nullable output positions
        // throw ZeroAllocOrmMaterializationException on DBNull instead (#244) —
        // the adopter opts in to NULL tolerance by declaring `T?`.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InsertOrderMaybeCount")]
                public partial Task<(OrderRow Result, int? OptionalCount)> InsertAsync(
                    int customerId, int? optionalCount, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void SprocWithOutputParams_multi_result_set_plus_output_emits_NextResult_chain()
    {
        // Phase E review Fix 3 — exercise the interleaving of (a) multi-result
        // walks with NextResultAsync chaining between two result positions,
        // (b) a list materialization across the first result set, (c) a row
        // materialization across the second, and (d) an output parameter
        // readback after reader disposal. This is the highest-regression-risk
        // shape because it stresses both the drain-loop semantics and the
        // multi-result-set NextResult chain at the same emit-site.
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_GetHeadsAndInsertTail")]
                public partial Task<(IReadOnlyList<OrderRow> Heads, OrderRow Tail, int NewOrderId)> GetHeadsAndInsertTailAsync(
                    int customerId, int newOrderId, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void SprocWithOutputParams_non_nullable_outputs_emit_null_guard_naming_procedure_and_parameter()
    {
        // #244 — a non-nullable output left NULL by the procedure throws
        // ZeroAllocOrmMaterializationException naming the procedure and the
        // parameter, for a string, a value type, an enum, a string-stored enum
        // and a value object alike. Before, a string silently became "" through
        // Convert.ToString and a value type threw a bare InvalidCastException.
        // The nullable elements keep their `is DBNull ? null : ...` readback.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public enum Status { Pending, Cancelled }
            [StoreAsString] public enum Named { Pending, Cancelled }
            public sealed record OrderId(int Value);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("dbo.usp_Outputs")]
                public partial Task<(string Label, int Count, Status State, Named Name, OrderId OrderRef, string? Note, int? Maybe)> RunAsync(
                    string label, int count, Status state, Named name, OrderId orderRef, string? note, int? maybe, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }
}
