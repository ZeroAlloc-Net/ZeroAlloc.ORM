namespace ZeroAlloc.ORM;

/// <summary>
/// Customizes how a row (or scalar) is materialized into a CLR type. Applied on
/// a type declaration (record/struct/class) or on a partial method's return value
/// (<c>[return: Materialize(Factory = "...")]</c>). v0.5 makes the <see cref="Factory"/>
/// path active: the generator looks up a <c>static</c> method of the given name on
/// the target type and emits a direct invocation in place of the convention-discovered
/// constructor call (see v1.0 design Section 3, discovery-order rule #1 — explicit
/// always wins).
/// </summary>
[AttributeUsage(AttributeTargets.ReturnValue
              | AttributeTargets.Class
              | AttributeTargets.Struct)]
public sealed class MaterializeAttribute : Attribute
{
    /// <summary>
    /// Materialization strategy hint. <see cref="MaterializeStrategy.Auto"/> (the default)
    /// keeps convention discovery in charge. The other values are reserved for future
    /// milestones — v0.5 honors only <see cref="Factory"/>; the Strategy enum is parsed
    /// from source so adopters can express intent today even when the generator does
    /// not yet branch on it.
    /// </summary>
    public MaterializeStrategy Strategy { get; init; } = MaterializeStrategy.Auto;

    /// <summary>
    /// Name of a <c>static</c> method on the annotated type the generator should call to
    /// build instances. The method's parameter list must match the column / inner-field
    /// list (by name, case-insensitive); ZAO043 fires when the named method is missing
    /// or non-static.
    /// </summary>
    public string? Factory { get; init; }
}
