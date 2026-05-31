namespace ZeroAlloc.ORM;

/// <summary>
/// Selects what a <see cref="CommandAttribute"/>-annotated method returns. The
/// generator dispatches on this value to pick between <c>ExecuteNonQueryAsync</c>
/// (rows-affected count), <c>ExecuteScalarAsync</c> (typed scalar), and the
/// identity-fetch emit shape. See the v1.0 design Section 2 for the full table.
/// </summary>
public enum CommandKind
{
    /// <summary>Execute via <c>ExecuteNonQueryAsync</c>; return the rows-affected count (default).</summary>
    NonQuery,

    /// <summary>Execute via <c>ExecuteScalarAsync</c>; materialize the result to the declared return type.</summary>
    Scalar,

    /// <summary>
    /// Identity-fetch shape: the SQL is expected to terminate with a provider-specific
    /// identity suffix (<c>RETURNING "Id"</c> for Postgres/Sqlite, <c>; SELECT SCOPE_IDENTITY()</c>
    /// for SQL Server) and the generator emits a scalar materialization of the resulting value.
    /// </summary>
    Identity,
}
