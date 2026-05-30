namespace ZeroAlloc.ORM;

/// <summary>
/// Marks a partial method as a stored-procedure invocation. Procedures default to
/// <see cref="BatchMode.Never"/> because most providers don't support batching procedure
/// calls and the call shape is already a single round-trip.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StoredProcedureAttribute(string procedureName) : Attribute
{
    /// <summary>Fully-qualified procedure name (e.g. <c>"dbo.GetOrderById"</c>).</summary>
    public string ProcedureName { get; } = procedureName;

    /// <summary>Multi-statement execution strategy. Defaults to <see cref="BatchMode.Never"/>.</summary>
    public BatchMode Batch { get; init; } = BatchMode.Never;
}
