namespace ZeroAlloc.ORM;

/// <summary>
/// Overrides how the generator materializes a result row. Can be applied to the return
/// value of a [Query]/[Command] method, to a generic type parameter, or to the target
/// type itself (class or struct).
/// </summary>
[AttributeUsage(
    AttributeTargets.ReturnValue
    | AttributeTargets.GenericParameter
    | AttributeTargets.Class
    | AttributeTargets.Struct)]
public sealed class MaterializeAttribute : Attribute
{
    /// <summary>Materialization mode. Defaults to <see cref="MaterializeStrategy.Auto"/>.</summary>
    public MaterializeStrategy Strategy { get; init; } = MaterializeStrategy.Auto;

    /// <summary>
    /// Fully-qualified name of a user-provided factory method when <see cref="Strategy"/> is
    /// <see cref="MaterializeStrategy.Custom"/>. Ignored otherwise.
    /// </summary>
    public string? Factory { get; init; }
}
