using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

// v0.6 Phase A.3 — Postgres-targeted repo for [StoredProcedure] + procedure-
// via-CALL + function-via-SELECT round-trip coverage. Lives next to the
// Postgres test classes (StoredProcedureTests, etc.) so the seed DDL +
// signatures stay co-located with the assertions.
//
// Method shapes covered:
//
//   * GetOrderViaFunctionAsync         — Postgres FUNCTION returning rowset,
//                                        invoked via [Query] (the cookbook
//                                        recommended path; Postgres procedures
//                                        returning rowsets are awkward — CALL
//                                        + refcursor needs an in-transaction
//                                        FETCH dance — so the idiom for
//                                        "stored logic returning rows" on PG
//                                        is a FUNCTION via Query).
//   * AllocateIdAsync                  — output-only [StoredProcedure] with
//                                        named-tuple output param via Postgres
//                                        CREATE PROCEDURE + INOUT. Postgres
//                                        15+ supports OUT parameters on
//                                        procedures; 14 supported only INOUT.
//                                        We use INOUT for broad compatibility.
//   * GetOrdersAndCountAsync           — multi-result-set via TWO function
//                                        invocations joined with `;`. The
//                                        functions encapsulate the procedure-
//                                        like logic; the [Query] BatchMode
//                                        controls whether it's IAsyncDbBatch
//                                        or `;`-joined at the wire level.
public sealed partial class StoredProcedureRepo(IAsyncDbConnection connection)
{
    // Procedure-via-FUNCTION path (Postgres-idiomatic for rowset returns).
    [Query("SELECT id, customerid, total FROM get_order_fn(@id)")]
    public partial Task<OrderRow?> GetOrderViaFunctionAsync(int id, CancellationToken ct);

    // Real [StoredProcedure] path against a Postgres CREATE PROCEDURE with
    // OUT parameters (PG 15+; the fixture pins postgres:16-alpine).
    //
    // Two coupled Postgres quirks shaped this signature:
    //
    //   1. Postgres folds unquoted identifiers to lowercase. The C# parameter
    //      names + tuple-field names use all-lowercase forms so they match
    //      the procedure's resolved parameter names without quoting in the
    //      DDL. With camelCase names, Npgsql's CALL-with-named-args overload
    //      resolution falls back to `unknown` types and the procedure lookup
    //      fails with 42883 ("procedure ... does not exist").
    //
    //   2. The ZA.ORM generator emits `Direction = Output` (NOT InputOutput)
    //      for named-tuple output slots. Combined with Postgres's procedure
    //      mechanics, this requires the procedure to declare its outputs as
    //      OUT (pure output) rather than INOUT — otherwise the C#-side
    //      omission of a value-write surfaces as DBNull on the readback.
    //      OUT requires PG 15+, which matches the fixture pin. Since 2.0,
    //      `[Param(Direction = InputOutput)]` binds an INOUT parameter with
    //      its value; see InputOutputAsync below.
    //
    // The named-tuple convention matches `Neworderid` against the
    // `neworderid` parameter (case-insensitive) and flips its Direction to
    // Output; same for `Status`.
    [StoredProcedure("allocate_id_proc")]
    public partial Task<(int Neworderid, int Status)> AllocateIdAsync(
        int neworderid,
        int status,
        CancellationToken ct);

    // v2.0, #235 — the SQL Server output-type shape on a Postgres procedure.
    // The generator now sets DbType on every output and Size = -1 on the text
    // output. For an OUT argument Npgsql writes NULL into the CALL and matches the
    // returned row by name, so neither changes what comes back. `rounded` carries
    // Scale = 0, the scale SQL Server rounds to; Postgres ignores it and returns
    // the exact value, and the test pins that.
    [StoredProcedure("output_types_proc")]
    public partial Task<(int Count, string Label, decimal Total, decimal Rounded, Guid Traceid, DateTime At)> OutputTypesAsync(
        int seed,
        int count,
        string label,
        [Param(Precision = 18, Scale = 4)] decimal total,
        [Param(Scale = 0)] decimal rounded,
        Guid traceid,
        DateTime at,
        CancellationToken ct);

    // v2.0, #235 — INOUT parameters, bound as ParameterDirection.InputOutput so
    // the argument reaches the procedure.
    [StoredProcedure("input_output_proc")]
    public partial Task<(int Counter, string Label)> InputOutputAsync(
        [Param(Direction = System.Data.ParameterDirection.InputOutput)] int counter,
        [Param(Direction = System.Data.ParameterDirection.InputOutput)] string label,
        CancellationToken ct);

    // #244 — null_output_proc leaves all four outputs NULL. Each method
    // declares exactly one of them non-nullable: a string, an int, an enum and a
    // value object. The last method declares all four nullable.
    [StoredProcedure("null_output_proc")]
    public partial Task<(string Note, int? Amount, Status? State, OrderId? Orderref)> NullIntoStringAsync(
        string note, int? amount, Status? state, OrderId? orderref, CancellationToken ct);

    [StoredProcedure("null_output_proc")]
    public partial Task<(string? Note, int Amount, Status? State, OrderId? Orderref)> NullIntoIntAsync(
        string? note, int amount, Status? state, OrderId? orderref, CancellationToken ct);

    [StoredProcedure("null_output_proc")]
    public partial Task<(string? Note, int? Amount, Status State, OrderId? Orderref)> NullIntoEnumAsync(
        string? note, int? amount, Status state, OrderId? orderref, CancellationToken ct);

    [StoredProcedure("null_output_proc")]
    public partial Task<(string? Note, int? Amount, Status? State, OrderId Orderref)> NullIntoValueObjectAsync(
        string? note, int? amount, Status? state, OrderId orderref, CancellationToken ct);

    [StoredProcedure("null_output_proc")]
    public partial Task<(string? Note, int? Amount, Status? State, OrderId? Orderref)> NullIntoNullableAsync(
        string? note, int? amount, Status? state, OrderId? orderref, CancellationToken ct);

    // #241 — the RETURN_VALUE convention on Postgres, which has no procedure
    // RETURN value. The generator cannot see the provider, so it emits the same
    // ReturnValue parameter as for SQL Server; the test pins what Npgsql does.
    [StoredProcedure("return_value_proc")]
    public partial Task<(int Doubled, int? RETURN_VALUE)> ReturnValueAsync(
        int seed,
        int doubled,
        int? RETURN_VALUE,
        CancellationToken ct);

    // #241 — a procedure that declares an OUT parameter named RETURN_VALUE.
    // Without a written Direction the convention binds it as ReturnValue.
    [StoredProcedure("declared_return_value_proc")]
    public partial Task<(int Doubled, int RETURN_VALUE)> DeclaredReturnValueByConventionAsync(
        int seed,
        int doubled,
        int RETURN_VALUE,
        CancellationToken ct);

    // The escape hatch: a written Direction = Output keeps it an OUT argument.
    [StoredProcedure("declared_return_value_proc")]
    public partial Task<(int Doubled, int RETURN_VALUE)> DeclaredReturnValueAsOutputAsync(
        int seed,
        int doubled,
        [Param(Direction = System.Data.ParameterDirection.Output)] int RETURN_VALUE,
        CancellationToken ct);

    // Multi-result-set via two function calls joined with `;`. Auto-batch
    // mode lets the runtime pick the IAsyncDbBatch path on Postgres
    // (CanCreateBatch == true on Npgsql). Functions encapsulate the same
    // logic a procedure would on SQL Server; on PG this is the idiomatic
    // way to "run two related result sets" in one round-trip.
    [Query(
        "SELECT COUNT(*)::int FROM orders; SELECT id, customerid, total FROM orders ORDER BY id",
        Batch = BatchMode.Auto)]
    public partial Task<(int Count, IReadOnlyList<OrderRow> All)?> GetOrdersAndCountAsync(
        CancellationToken ct);
}
