namespace ZeroAlloc.ORM;

/// <summary>
/// Marks a partial method as a SQL query. The generator emits the materialization
/// pipeline against the containing type's <c>IAsyncDbConnection</c>. See the v1.0
/// design Section 2 for return-type dispatch rules and supported method shapes.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class QueryAttribute(string sql) : Attribute
{
    /// <summary>The SQL fragment, or — when <see cref="FromResource"/> is true — the embedded resource name.</summary>
    public string Sql { get; } = sql;

    /// <summary>
    /// When true, <see cref="Sql"/> names an embedded resource (e.g. <c>"MyApp.Sql.GetOrderById"</c>)
    /// rather than inline SQL. Resource lookup happens at generator time.
    /// </summary>
    public bool FromResource { get; init; }

    /// <summary>
    /// Multi-statement strategy. <see cref="BatchMode.Auto"/> (the default) picks <c>IAsyncDbBatch</c>
    /// when the provider supports it and the SQL has multiple statements; otherwise falls back to
    /// a single command with <c>NextResultAsync</c>.
    /// </summary>
    public BatchMode Batch { get; init; } = BatchMode.Auto;

    /// <summary>
    /// How many rows the method usually returns, for a method that returns a list. The generated list is
    /// created with this capacity, so a result of up to that many rows never grows it. Zero, the default,
    /// leaves the list unsized, which grows as rows arrive.
    /// </summary>
    /// <remarks>
    /// The hint is capped at 1024, so a large value that is usually left unfilled does not allocate a large
    /// empty array up front. It sizes the one list of a list-returning method; a tuple result reads each of its
    /// lists unsized.
    /// </remarks>
    public int ExpectedRows { get; init; }
}
