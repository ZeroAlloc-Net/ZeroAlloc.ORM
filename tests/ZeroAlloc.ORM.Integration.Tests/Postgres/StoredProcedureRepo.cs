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
//   * CountOrdersForCustomerAsync      — function returning a scalar, via
//                                        [Query] with Kind=Scalar-like SELECT
//                                        that materializes the scalar through
//                                        the single-row tuple shape. Not used
//                                        in tests (kept for symmetry with the
//                                        cookbook example), removed for now.
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
    //      OUT requires PG 15+, which matches the fixture pin.
    //
    // The named-tuple convention matches `Neworderid` against the
    // `neworderid` parameter (case-insensitive) and flips its Direction to
    // Output; same for `Status`.
    [StoredProcedure("allocate_id_proc")]
    public partial Task<(int Neworderid, int Status)> AllocateIdAsync(
        int neworderid,
        int status,
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
