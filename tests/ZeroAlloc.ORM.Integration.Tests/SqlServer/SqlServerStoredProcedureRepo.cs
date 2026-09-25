using System.Data;
using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests.SqlServer;

// v2.0, #235 — generated [StoredProcedure] output parameters on SQL Server.
//
// SqlClient validates every output parameter before it sends the RPC. Without a
// DbType it infers NVarChar, and an NVarChar with Size 0 throws "the Size
// property has an invalid size of 0", so before #235 every method here failed.
// The generator now sets DbType from the tuple element's type and Size = -1,
// which is MAX, on string outputs.
public sealed partial class SqlServerStoredProcedureRepo(IAsyncDbConnection connection)
{
    // One output of each type. `total` carries the procedure's DECIMAL(18,4)
    // facets. `rounded` is declared the same way in the procedure but with
    // Scale = 0 here, which is how SqlClient declares a decimal output that has
    // no Scale, the case ZAO065 warns about: the value comes back rounded. The
    // test pins that behaviour.
    [StoredProcedure("dbo.output_types_proc")]
    public partial Task<(int Count, string Label, decimal Total, decimal Rounded, Guid TraceId, DateTime At)> OutputTypesAsync(
        int seed,
        int count,
        string label,
        [Param(Precision = 18, Scale = 4)] decimal total,
        [Param(Scale = 0)] decimal rounded,
        Guid traceId,
        DateTime at,
        CancellationToken ct);

    // The remaining output types: byte[] defaults to Size = -1 like string, and
    // DateTimeOffset, TimeSpan and the two enum conventions get their DbType
    // through the primitive they read as.
    [StoredProcedure("dbo.more_output_types_proc")]
    public partial Task<(byte[] Blob, DateTimeOffset Stamp, TimeSpan Span, Status State, StringStatus Named)> MoreOutputTypesAsync(
        byte[] blob,
        DateTimeOffset stamp,
        TimeSpan span,
        Status state,
        StringStatus named,
        CancellationToken ct);

    // #245, #247 — the temporal outputs PostgresTemporalOutputTests covers.
    // SqlClient hands each one back as the tuple element's own type: datetimeoffset
    // as DateTimeOffset, time as TimeSpan and date as DateTime.
    [StoredProcedure("dbo.temporal_output_proc")]
    public partial Task<(DateTimeOffset Stamp, DateTimeOffset? Missing, TimeSpan Clock, TimeSpan? Late, DateTime Day)> TemporalOutputsAsync(
        DateTimeOffset stamp,
        DateTimeOffset? missing,
        TimeSpan clock,
        TimeSpan? late,
        DateTime day,
        CancellationToken ct);

    // A scalar command reads through the same conversion as an output.
    [Command("SELECT CAST('2024-01-02T03:04:05.1234567+02:00' AS DATETIMEOFFSET(7))", Kind = CommandKind.Scalar)]
    public partial Task<DateTimeOffset> ScalarStampAsync(CancellationToken ct);

    [Command("SELECT CAST('13:14:15.1234567' AS TIME(7))", Kind = CommandKind.Scalar)]
    public partial Task<TimeSpan> ScalarClockAsync(CancellationToken ct);

    // Nullable outputs: the procedure leaves `missing` and `note` NULL and sets
    // `present`.
    [StoredProcedure("dbo.nullable_output_proc")]
    public partial Task<(int? Missing, string? Note, int? Present)> NullableOutputsAsync(
        int? missing,
        string? note,
        int? present,
        CancellationToken ct);

    // #244 — dbo.null_output_proc leaves all four outputs NULL. Each method
    // declares exactly one of them non-nullable: a string, an int, an enum and a
    // value object. The last method declares all four nullable.
    [StoredProcedure("dbo.null_output_proc")]
    public partial Task<(string Note, int? Amount, Status? State, OrderId? Orderref)> NullIntoStringAsync(
        string note, int? amount, Status? state, OrderId? orderref, CancellationToken ct);

    [StoredProcedure("dbo.null_output_proc")]
    public partial Task<(string? Note, int Amount, Status? State, OrderId? Orderref)> NullIntoIntAsync(
        string? note, int amount, Status? state, OrderId? orderref, CancellationToken ct);

    [StoredProcedure("dbo.null_output_proc")]
    public partial Task<(string? Note, int? Amount, Status State, OrderId? Orderref)> NullIntoEnumAsync(
        string? note, int? amount, Status state, OrderId? orderref, CancellationToken ct);

    [StoredProcedure("dbo.null_output_proc")]
    public partial Task<(string? Note, int? Amount, Status? State, OrderId Orderref)> NullIntoValueObjectAsync(
        string? note, int? amount, Status? state, OrderId orderref, CancellationToken ct);

    [StoredProcedure("dbo.null_output_proc")]
    public partial Task<(string? Note, int? Amount, Status? State, OrderId? Orderref)> NullIntoNullableAsync(
        string? note, int? amount, Status? state, OrderId? orderref, CancellationToken ct);

    // Fixed-length outputs, NCHAR and CHAR. There is no MAX form of either, so
    // the generator gives them no default size and ZAO066 requires one.
    [StoredProcedure("dbo.fixed_length_proc")]
    public partial Task<(string Code, string AnsiCode)> FixedLengthAsync(
        [Param(DbType = DbType.StringFixedLength, Size = 10)] string code,
        [Param(DbType = DbType.AnsiStringFixedLength, Size = 4)] string ansiCode,
        CancellationToken ct);

    // Input-output parameters: the argument is sent as the initial value and the
    // value the procedure leaves behind is read back.
    [StoredProcedure("dbo.input_output_proc")]
    public partial Task<(int Counter, string Label)> InputOutputAsync(
        [Param(Direction = ParameterDirection.InputOutput)] int counter,
        [Param(Direction = ParameterDirection.InputOutput)] string label,
        CancellationToken ct);

    // The same shape as PostgresParameterBindingProcedureRepo.ScaleAsync: one
    // input and two outputs, declared in the procedure's order.
    [StoredProcedure("dbo.scale_proc")]
    public partial Task<(int Doubled, int Tripled)> ScaleAsync(
        int amount,
        int doubled,
        int tripled,
        CancellationToken ct);

    // #241 — the procedure's RETURN value, beside an output parameter and a
    // result set. SQL Server fills it only into a ParameterDirection.ReturnValue
    // parameter; before #241 the RETURN_VALUE field bound as Output and the call
    // failed, because the procedure declares no @RETURN_VALUE parameter.
    [StoredProcedure("dbo.return_value_proc")]
    public partial Task<(OrderRow Row, int Doubled, int RETURN_VALUE)> ReturnValueByConventionAsync(
        int seed,
        int doubled,
        int RETURN_VALUE,
        CancellationToken ct);

    // The same procedure through the explicit Direction, into an int? field.
    [StoredProcedure("dbo.return_value_proc")]
    public partial Task<(int Doubled, OrderRow Row, int? Status)> ReturnValueByDirectionAsync(
        int seed,
        int doubled,
        [Param(Direction = ParameterDirection.ReturnValue)] int? status,
        CancellationToken ct);

    // No result set: the RETURN value and the output parameter come back through
    // ExecuteNonQueryAsync.
    [StoredProcedure("dbo.return_value_only_proc")]
    public partial Task<(int Doubled, int RETURN_VALUE)> ReturnValueOutputOnlyAsync(
        int seed,
        int doubled,
        int RETURN_VALUE,
        CancellationToken ct);

    // A procedure that really declares an @RETURN_VALUE OUTPUT parameter keeps it
    // an output parameter by writing the Direction.
    [StoredProcedure("dbo.declared_return_value_proc")]
    public partial Task<(int Doubled, int RETURN_VALUE)> DeclaredReturnValueParameterAsync(
        int seed,
        int doubled,
        [Param(Direction = ParameterDirection.Output)] int RETURN_VALUE,
        CancellationToken ct);

    // The same procedure with the C# parameters in the reverse order, so the
    // generated parameters are added out of declaration order.
    [StoredProcedure("dbo.scale_proc")]
    public partial Task<(int Tripled, int Doubled)> ScaleReversedAsync(
        int tripled,
        int doubled,
        int amount,
        CancellationToken ct);
}
