namespace ZeroAlloc.ORM;

/// <summary>
/// Declares the SQL dialect a repository runs against, so the generator can check its
/// parameters against that provider at compile time.
/// </summary>
/// <remarks>
/// <para>
/// Put it on a repository, the type that declares the <see cref="QueryAttribute"/>,
/// <see cref="CommandAttribute"/> and <see cref="StoredProcedureAttribute"/> methods, or on
/// the assembly as the default for every repository in it:
/// <c>[assembly: Dialect(SqlDialect.PostgreSql)]</c>. A repository's own attribute wins over
/// the assembly default. Declaring a dialect is optional; without one the generator reports
/// only what holds on every provider.
/// </para>
/// <para>
/// With a declared dialect, ZAO015 reports a <c>[Param(DbType = ...)]</c> that the dialect's
/// provider rejects for the parameter's type, such as <c>DbType.Guid</c> on an <c>int</c> on
/// SQL Server. The attribute changes no generated code.
/// </para>
/// <para>
/// Only the repository type's own attribute counts. One on a base type or on a type that
/// contains the repository does not apply to it.
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = false)]
public sealed class DialectAttribute(SqlDialect dialect) : Attribute
{
    /// <summary>The declared dialect.</summary>
    public SqlDialect Dialect { get; } = dialect;
}
