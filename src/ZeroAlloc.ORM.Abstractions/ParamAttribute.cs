using System.Data;

namespace ZeroAlloc.ORM;

/// <summary>
/// Customizes how a partial-method parameter is bound to its <c>DbParameter</c>.
/// When <see cref="Name"/> is null the generator uses the C# parameter name.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ParamAttribute : Attribute
{
    /// <summary>Override DB parameter name. When null, the C# parameter name is used.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Override the inferred <see cref="System.Data.DbType"/>. Defaults to <see cref="DbType.Object"/>
    /// which signals "let the provider infer from the CLR type".
    /// </summary>
    public DbType DbType { get; init; } = DbType.Object;
}
