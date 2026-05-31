namespace ZeroAlloc.ORM;

/// <summary>
/// Marks a partial method as a stored-procedure invocation. The generator emits the
/// open/execute/close lifecycle against the containing type's <c>IAsyncDbConnection</c>
/// with <c>CommandType.StoredProcedure</c> and <c>CommandText</c> set to
/// <see cref="ProcedureName"/>. Return-type dispatch follows the same shape table as
/// <see cref="QueryAttribute"/> — single-row, scalar, list, tuple-multi-result, etc. —
/// so a sproc can drop into any single-result-set or multi-result-set return shape
/// without changes to the materialization templates. See the v1.0 design Section 2.5.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StoredProcedureAttribute(string procedureName) : Attribute
{
    /// <summary>The procedure name passed verbatim to <c>DbCommand.CommandText</c>.</summary>
    public string ProcedureName { get; } = procedureName;

    /// <summary>
    /// Multi-statement strategy. Stored procedures already encapsulate multi-statement
    /// behaviour server-side, so the default is <see cref="BatchMode.Never"/> — the
    /// emit treats the procedure call as a single <c>DbCommand</c> regardless of how
    /// many result sets it returns, with the materializer walking the reader via
    /// <c>NextResultAsync</c>. Setting <see cref="BatchMode.Always"/> on a sproc is
    /// generally meaningless (a sproc isn't a `;`-joined script that an <c>IAsyncDbBatch</c>
    /// can usefully decompose) and is accepted only for symmetry with
    /// <see cref="QueryAttribute"/>.
    /// </summary>
    public BatchMode Batch { get; init; } = BatchMode.Never;
}
