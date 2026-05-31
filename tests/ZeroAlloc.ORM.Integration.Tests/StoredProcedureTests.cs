using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// v0.4 Phase D — integration coverage for [StoredProcedure] is DEFERRED to Phase G
// (Postgres fixture in v0.6). Sqlite — the integration backend in v0.4 — has no
// native stored-procedure support: there's no CREATE PROCEDURE syntax, and the
// closest equivalents (views, triggers, table-valued functions) all require the
// caller to use SELECT/INSERT statements rather than `EXEC procname` /
// CommandType.StoredProcedure. Routing a Sqlite call through CommandType =
// StoredProcedure with CommandText = "my_view" surfaces "SQLite Error 1: 'no
// such function: my_view'" — the driver passes the text through to the parser
// which expects a function-call statement, not a procedure invocation.
//
// Snapshot + compile-smoke coverage in ZeroAlloc.ORM.Generator.Tests
// (StoredProcedureEmitTests, StoredProcedureMultiResultTests,
// CompileSmokeTests.StoredProcedure_*_emit_compiles_cleanly) verify the emit
// shape end-to-end. Real-server integration lands when Phase G adds the
// Postgres fixture (`CREATE PROCEDURE` + `CALL`) or a SQL Server fixture
// (`CREATE PROC` + `EXEC`).
public class StoredProcedureTests
{
    [Fact(Skip = "v0.4 Phase D: Sqlite has no native stored procedures. Integration coverage deferred to Phase G with the Postgres / SQL Server fixture. Snapshot tests in StoredProcedureEmitTests verify the emit shape.")]
    public void StoredProcedure_integration_deferred_to_phase_G()
    {
        // Placeholder so the deferral is visible in the test output.
    }
}
