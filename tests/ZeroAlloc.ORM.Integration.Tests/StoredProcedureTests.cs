using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests;

// v0.4 Phase D — [StoredProcedure] integration coverage on a real-server
// provider was DEFERRED until the v0.6 Postgres fixture landed. Sqlite has
// no native stored-procedure support: there's no CREATE PROCEDURE syntax,
// and the closest equivalents (views, triggers, table-valued functions) all
// require the caller to use SELECT/INSERT statements rather than `EXEC` /
// `CALL` / `CommandType.StoredProcedure`.
//
// v0.6 Phase A.3 RESOLVED this deferral — see
// `Postgres/StoredProcedureTests.cs` for the real-server round-trip suite
// covering:
//   * Function-via-[Query] for rowset returns (Postgres-idiomatic path).
//   * CREATE PROCEDURE + CALL with INOUT named-tuple output parameter via
//     [StoredProcedure], proving the v0.4 output-param emit lights up.
//   * Multi-result-set via function calls under BatchMode.Auto (also
//     resolves v0.3-CLN3 — IAsyncDbBatch runtime branch finally exercised).
//
// This Sqlite placeholder stays as a documentation trail: a future Sqlite
// adopter discovering `[StoredProcedure]` against Sqlite hits this file
// first and learns why the integration suite redirects to Postgres.
[Trait("Provider", "Sqlite")]
public class StoredProcedureTests
{
    [Fact(Skip = "Sqlite has no native stored procedures. Integration coverage lives in tests/ZeroAlloc.ORM.Integration.Tests/Postgres/StoredProcedureTests.cs (v0.6 Phase A.3).")]
    public void StoredProcedure_integration_lives_in_postgres_suite()
    {
        // Placeholder so the deferral context is visible in dotnet test output.
    }
}
