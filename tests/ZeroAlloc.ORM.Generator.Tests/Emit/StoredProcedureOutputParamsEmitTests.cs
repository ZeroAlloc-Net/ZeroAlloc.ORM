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

    // #241 — a RETURN value beside an output parameter and a result row. The
    // RETURN_VALUE parameter binds as ReturnValue, gets no Value, and is checked
    // for null before the readback: only SQL Server sets it.
    [Fact]
    public void SprocWithOutputParams_return_value_emits_ReturnValue_direction_and_null_check()
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
                public partial Task<(OrderRow Result, int NewOrderId, int RETURN_VALUE)> InsertAsync(
                    int customerId, int newOrderId, int RETURN_VALUE, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    // #241 — the int? form: the unset-parameter check stays, and a database NULL
    // reads as null through the readback's DBNull guard instead of throwing.
    [Fact]
    public void SprocWithOutputParams_nullable_return_value_reads_NULL_as_null()
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
                public partial Task<(OrderRow Result, int NewOrderId, int? RETURN_VALUE)> InsertAsync(
                    int customerId, int newOrderId, int? RETURN_VALUE, CancellationToken ct);
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
        // procedure leaves the output parameter at DBNull. The `is null or
        // DBNull ? null : ...` ternary keeps the contract symmetric with scalar
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
        // The nullable elements keep their `is null or DBNull ? null : ...`
        // readback.
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

    [Fact]
    public void SprocWithOutputParams_null_guard_names_the_bound_parameter_name_override()
    {
        // #244 review — with [Param(Name = "new_id")] the DbParameter is bound as
        // `new_id`, so the NULL-output message must name `new_id`, the parameter
        // the procedure declares, not the C# name `newId`.
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("dbo.usp_AllocateId")]
                public partial Task<(int NewId, int? Status)> AllocateAsync(
                    [Param(Name = "new_id")] int newId, int? status, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void SprocWithOutputParams_temporal_outputs_convert_from_provider_default_types()
    {
        // #245, #247 â€” a boxed output carries the provider's default CLR type for
        // the column, which is not always the tuple element's type: Npgsql hands
        // back timestamptz as DateTime, time as TimeOnly and date as DateOnly.
        // Each temporal readback converts those the way GetFieldValue<T> does on
        // the reader path. The nullable pair pins the DBNull guard around it.
        var source = """
            using System;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_Temporal")]
                public partial Task<(DateTime Day, DateTimeOffset Stamp, TimeSpan Span, DateTimeOffset? MaybeStamp, TimeSpan? MaybeSpan)> TemporalAsync(
                    DateTime day, DateTimeOffset stamp, TimeSpan span, DateTimeOffset? maybeStamp, TimeSpan? maybeSpan, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void SprocWithOutputParams_inout_timespan_leaves_DbType_unset()
    {
        // #255 — an InputOutput TimeSpan writes its initial value before
        // execution, unlike a pure Output parameter. Declaring DbType.Time
        // makes Npgsql encode that value as a Postgres `time`, which rejects
        // 24 hours or more ("time out of range"); a Postgres `interval`
        // parameter has no such ceiling. Leaving DbType unset lets each
        // provider infer its native type from the CLR value instead:
        // NpgsqlDbType.Interval on Npgsql, DbType.Time on SqlClient. An
        // Output-only TimeSpan (no initial value to infer from) still
        // declares DbType.Time explicitly.
        var source = """
            using System;
            using System.Data;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InOutSpan")]
                public partial Task<(TimeSpan Span, TimeSpan Fixed)> RunAsync(
                    [Param(Direction = ParameterDirection.InputOutput)] TimeSpan span,
                    TimeSpan @fixed,
                    CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void SprocWithOutputParams_inout_timeonly_still_declares_DbType_Time()
    {
        // #255 review — DbTypeName is "Time" for both TimeSpan and TimeOnly
        // (#256), so the InputOutput-unset rule above must key on the actual
        // element type, not the shared DbType name, or it would also unset
        // DbType for an InputOutput TimeOnly. TimeOnly cannot exceed 24 hours
        // by construction, so it has no interval ceiling to work around, and
        // it keeps the explicit DbType.Time an Output parameter always had.
        var source = """
            using System;
            using System.Data;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_InOutClock")]
                public partial Task<(TimeOnly Start, TimeOnly Fixed)> RunAsync(
                    [Param(Direction = ParameterDirection.InputOutput)] TimeOnly start,
                    TimeOnly @fixed,
                    CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public void SprocWithOutputParams_DateOnly_and_TimeOnly_outputs_declare_Date_and_Time()
    {
        // #256 — a DateOnly output declares DbType.Date and a TimeOnly output
        // DbType.Time. The boxed value is the provider's default CLR type for the
        // column: Npgsql returns DateOnly and TimeOnly, SqlClient DateTime and
        // TimeSpan, and Microsoft.Data.Sqlite TEXT or a Julian day number. Each
        // readback converts those the way GetFieldValue<T> does for the column.
        var source = """
            using System;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [StoredProcedure("usp_Shift")]
                public partial Task<(DateOnly Day, TimeOnly Start, DateOnly? MaybeDay, TimeOnly? MaybeStart)> ShiftAsync(
                    DateOnly day, TimeOnly start, DateOnly? maybeDay, TimeOnly? maybeStart, CancellationToken ct);
            }
            """;
        GeneratorSnapshot.Verify(GeneratorHarness.RunGenerator(source));
    }
}
