namespace ZeroAlloc.ORM.Generator.Model;

// Per-method emit-template selector for SQL batch handling. Resolved from the
// statement count of the [Query] body plus the attribute's BatchMode option
// (0=Auto, 1=Always, 2=Never per the BatchMode enum int values shipped in
// v0.2). Populated on QueryMethodModel during TransformMethod; v0.3 Phase B
// wires the emit consumer.
internal enum BatchEmitStrategy
{
    // Single-statement SQL — regular ExecuteReaderAsync, no batch involvement.
    SingleCommand,

    // Multi-statement SQL with BatchMode.Auto — emit both paths and runtime-
    // branch on DbConnection.CanCreateBatch (DbBatch when supported, falling
    // back to a single ;-joined command otherwise).
    BatchWithFallback,

    // Multi-statement SQL with BatchMode.Always — emit only the DbBatch path.
    // Caller has explicitly opted out of the joined-statements fallback.
    BatchAlways,

    // Multi-statement SQL with BatchMode.Never — emit only the ;-joined
    // single-command path; never touch DbBatch.
    JoinedStatementsOnly,
}
