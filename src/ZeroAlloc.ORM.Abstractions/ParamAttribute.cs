using System.Data;

namespace ZeroAlloc.ORM;

/// <summary>
/// Customizes how a partial-method parameter is bound to its <c>DbParameter</c>.
/// When <see cref="Name"/> is null the generator uses the C# parameter name.
/// </summary>
/// <remarks>
/// <see cref="Size"/>, <see cref="Precision"/> and <see cref="Scale"/> are applied only when
/// written. The generator copies them onto the <c>DbParameter</c> for input, output and
/// input-output parameters alike, and the provider decides what each one means for the
/// parameter's type. They and <see cref="DbType"/> are not supported on composite parameters,
/// which bind as several <c>DbParameter</c>s, and no member applies to a
/// <c>CancellationToken</c>, transaction or BulkInsert collection parameter, which bind none.
/// ZAO066 reports each of these.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ParamAttribute : Attribute
{
    /// <summary>
    /// Override DB parameter name. When null, the C# parameter name is used.
    /// </summary>
    /// <remarks>
    /// Give the bare name, <c>"orderId"</c>. The SQL placeholder carries the provider's
    /// sigil, <c>@orderId</c> or <c>:orderId</c>, and the provider matches the bare
    /// parameter name to it. A single leading <c>@</c>, <c>:</c> or <c>$</c> is dropped,
    /// so <c>"@orderId"</c> binds the same way.
    /// </remarks>
    public string? Name { get; init; }

    /// <summary>
    /// Override the inferred <see cref="System.Data.DbType"/>. Defaults to <see cref="DbType.Object"/>
    /// which signals "let the provider infer from the CLR type".
    /// </summary>
    /// <remarks>
    /// An input parameter without an override sets no <c>DbType</c>, and the provider infers
    /// one from the value. An output or input-output parameter always gets a <c>DbType</c>,
    /// inferred from the tuple element's type unless this override is written.
    /// </remarks>
    public DbType DbType { get; init; } = DbType.Object;

    /// <summary>
    /// The <c>DbParameter.Size</c>: the maximum length of a string or binary value.
    /// </summary>
    /// <remarks>
    /// Output and input-output <c>string</c> and <c>byte[]</c> parameters default to
    /// <c>-1</c>, which SQL Server reads as <c>MAX</c>. SQL Server rejects an output
    /// parameter of a string or binary type whose size is <c>0</c>, so ZAO066 reports
    /// <c>Size = 0</c> on one, and an output with a fixed-length <see cref="DbType"/>, such as
    /// <see cref="System.Data.DbType.StringFixedLength"/>, that has no <c>Size</c>. Npgsql and
    /// Microsoft.Data.Sqlite truncate an input value to a positive size and treat <c>-1</c> as
    /// no limit.
    /// </remarks>
    public int Size { get; init; }

    /// <summary>
    /// The <c>DbParameter.Precision</c>: the total number of digits of a <c>decimal</c> value.
    /// </summary>
    public byte Precision { get; init; }

    /// <summary>
    /// The <c>DbParameter.Scale</c>: the number of digits after the decimal point.
    /// </summary>
    /// <remarks>
    /// Set it on every <c>decimal</c> output parameter that SQL Server fills. SqlClient
    /// declares an output decimal without a scale as scale 0 and rounds the value the
    /// procedure assigns, so <c>1234.5678</c> comes back as <c>1235</c>. ZAO065 reports a
    /// <c>decimal</c> output without a scale.
    /// </remarks>
    public byte Scale { get; init; }

    /// <summary>
    /// The direction of a <c>[StoredProcedure]</c> output parameter.
    /// </summary>
    /// <remarks>
    /// A parameter whose name matches a field of the returned named tuple is bound as
    /// <see cref="ParameterDirection.Output"/>, and the argument passed for it is not sent.
    /// Write <see cref="ParameterDirection.InputOutput"/> to send the argument as the
    /// parameter's initial value and read the value the procedure leaves in it.
    /// Only <see cref="ParameterDirection.Output"/> and <see cref="ParameterDirection.InputOutput"/>
    /// are accepted, and only on a parameter that matches a tuple field; ZAO066 reports any
    /// other use.
    /// </remarks>
    public ParameterDirection Direction { get; init; } = ParameterDirection.Input;
}
