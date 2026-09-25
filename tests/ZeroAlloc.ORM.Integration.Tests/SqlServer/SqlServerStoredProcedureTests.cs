using System.Data;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.SqlServer;

// v2.0, #235 — generated stored-procedure output parameters on SQL Server 2022.
// See SqlServerStoredProcedureRepo for the failure these pin.
public sealed class SqlServerStoredProcedureTests : IAsyncLifetime
{
    private static readonly Guid TraceId = new("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    // DATETIME2(7) keeps all seven fractional digits. DbType.DateTime would
    // declare the parameter as the legacy DATETIME and round them to 1/300 s.
    private static readonly DateTime At = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Unspecified).AddTicks(1_234_567);

    private readonly SqlServerFixture _fx = new();

    public ValueTask InitializeAsync() => _fx.InitializeAsync();
    public ValueTask DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Output_parameters_of_each_type_round_trip()
    {
        await ExecuteAsync("""
            CREATE PROCEDURE dbo.output_types_proc
                @seed INT,
                @count INT OUTPUT,
                @label NVARCHAR(MAX) OUTPUT,
                @total DECIMAL(18,4) OUTPUT,
                @rounded DECIMAL(18,4) OUTPUT,
                @traceId UNIQUEIDENTIFIER OUTPUT,
                @at DATETIME2(7) OUTPUT
            AS
            BEGIN
                SET @count = @seed * 2;
                SET @label = REPLICATE(CAST(N'x' AS NVARCHAR(MAX)), 5000);
                SET @total = 1234.5678;
                SET @rounded = 1234.5678;
                SET @traceId = '6f9619ff-8b86-d011-b42d-00c04fc964ff';
                SET @at = '2024-01-02T03:04:05.1234567';
            END
            """);

        var repo = new SqlServerStoredProcedureRepo(_fx.Connection);
        var result = await repo.OutputTypesAsync(
            seed: 21, count: 0, label: "", total: 0m, rounded: 0m,
            traceId: Guid.Empty, at: default, CancellationToken.None);

        Assert.Equal(42, result.Count);
        // 5000 characters is past the 4000 of a sized NVARCHAR; Size = -1 is MAX.
        Assert.Equal(new string('x', 5000), result.Label);
        Assert.Equal(1234.5678m, result.Total);
        // Scale 0, which is also how SqlClient declares a decimal output without a
        // Scale: the value is rounded. This is what ZAO065 warns about.
        Assert.Equal(1235m, result.Rounded);
        Assert.Equal(TraceId, result.TraceId);
        Assert.Equal(At, result.At);
    }

    [Fact]
    public async Task Input_output_parameters_send_the_argument_and_read_the_result()
    {
        await ExecuteAsync("""
            CREATE PROCEDURE dbo.input_output_proc @counter INT OUTPUT, @label NVARCHAR(100) OUTPUT
            AS
            BEGIN
                SET @counter = @counter + 1;
                SET @label = @label + N'!';
            END
            """);

        var repo = new SqlServerStoredProcedureRepo(_fx.Connection);
        var (counter, label) = await repo.InputOutputAsync(41, "hi", CancellationToken.None);

        Assert.Equal(42, counter);
        Assert.Equal("hi!", label);
    }

    [Fact]
    public async Task Binary_datetimeoffset_timespan_and_enum_outputs_round_trip()
    {
        await ExecuteAsync("""
            CREATE PROCEDURE dbo.more_output_types_proc
                @blob VARBINARY(MAX) OUTPUT,
                @stamp DATETIMEOFFSET(7) OUTPUT,
                @span TIME(7) OUTPUT,
                @state INT OUTPUT,
                @named NVARCHAR(20) OUTPUT
            AS
            BEGIN
                SET @blob = CAST(REPLICATE(CAST(0xAB AS VARBINARY(MAX)), 9000) AS VARBINARY(MAX));
                SET @stamp = '2024-01-02T03:04:05.1234567+02:00';
                SET @span = '13:14:15.1234567';
                SET @state = 1;
                SET @named = N'Cancelled';
            END
            """);

        var repo = new SqlServerStoredProcedureRepo(_fx.Connection);
        var result = await repo.MoreOutputTypesAsync(
            blob: [], stamp: default, span: default, state: Status.Pending, named: StringStatus.Pending,
            CancellationToken.None);

        // 9000 bytes is past the 8000 of a sized VARBINARY; Size = -1 is MAX.
        Assert.Equal(9000, result.Blob.Length);
        Assert.All(result.Blob, b => Assert.Equal(0xAB, b));
        Assert.Equal(
            new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)).AddTicks(1_234_567),
            result.Stamp);
        Assert.Equal(TimeSpan.FromHours(13) + TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(15) + TimeSpan.FromTicks(1_234_567), result.Span);
        Assert.Equal(Status.Cancelled, result.State);
        Assert.Equal(StringStatus.Cancelled, result.Named);
    }

    [Fact]
    public async Task Temporal_outputs_round_trip()
    {
        await ExecuteAsync("""
            CREATE PROCEDURE dbo.temporal_output_proc
                @stamp DATETIMEOFFSET(7) OUTPUT,
                @missing DATETIMEOFFSET(7) OUTPUT,
                @clock TIME(7) OUTPUT,
                @late TIME(7) OUTPUT,
                @day DATE OUTPUT
            AS
            BEGIN
                SET @stamp = '2024-01-02T03:04:05.1234567-05:30';
                SET @missing = NULL;
                SET @clock = '13:14:15.1234567';
                SET @late = '23:59:59.9999999';
                SET @day = '2024-01-02';
            END
            """);

        var repo = new SqlServerStoredProcedureRepo(_fx.Connection);
        var result = await repo.TemporalOutputsAsync(
            stamp: default, missing: null, clock: default, late: null, day: default, CancellationToken.None);

        var stamp = new DateTimeOffset(2024, 1, 2, 3, 4, 5, new TimeSpan(-5, -30, 0)).AddTicks(1_234_567);
        Assert.Equal(stamp, result.Stamp);
        Assert.Equal(stamp.Offset, result.Stamp.Offset);
        Assert.Null(result.Missing);
        Assert.Equal(new TimeSpan(0, 13, 14, 15).Add(TimeSpan.FromTicks(1_234_567)), result.Clock);
        Assert.Equal(TimeSpan.FromDays(1) - TimeSpan.FromTicks(1), result.Late);
        Assert.Equal(new DateTime(2024, 1, 2), result.Day);
    }

    [Fact]
    public async Task Scalar_datetimeoffset_and_time_round_trip()
    {
        var repo = new SqlServerStoredProcedureRepo(_fx.Connection);

        var stamp = await repo.ScalarStampAsync(CancellationToken.None);
        var clock = await repo.ScalarClockAsync(CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)).AddTicks(1_234_567), stamp);
        Assert.Equal(TimeSpan.FromHours(2), stamp.Offset);
        Assert.Equal(new TimeSpan(0, 13, 14, 15).Add(TimeSpan.FromTicks(1_234_567)), clock);
    }

    [Fact]
    public async Task Nullable_outputs_left_NULL_read_back_as_null()
    {
        await ExecuteAsync("""
            CREATE PROCEDURE dbo.nullable_output_proc
                @missing INT OUTPUT,
                @note NVARCHAR(100) OUTPUT,
                @present INT OUTPUT
            AS
            BEGIN
                SET @missing = NULL;
                SET @note = NULL;
                SET @present = 7;
            END
            """);

        var repo = new SqlServerStoredProcedureRepo(_fx.Connection);
        var result = await repo.NullableOutputsAsync(1, "seed", null, CancellationToken.None);

        Assert.Null(result.Missing);
        Assert.Null(result.Note);
        Assert.Equal(7, result.Present);
    }

    // #244 — a NULL output into a non-nullable tuple element throws
    // ZeroAllocOrmMaterializationException naming the procedure and the
    // parameter. Before, a string became "" and a value type threw a bare
    // InvalidCastException from Convert.
    [Fact]
    public async Task Null_output_into_non_nullable_string_throws_naming_the_parameter()
    {
        await CreateNullOutputProcAsync();
        var repo = new SqlServerStoredProcedureRepo(_fx.Connection);

        await AssertNullOutputThrowsAsync(
            () => repo.NullIntoStringAsync("seed", null, null, null, CancellationToken.None), "note");
    }

    [Fact]
    public async Task Null_output_into_non_nullable_int_throws_naming_the_parameter()
    {
        await CreateNullOutputProcAsync();
        var repo = new SqlServerStoredProcedureRepo(_fx.Connection);

        await AssertNullOutputThrowsAsync(
            () => repo.NullIntoIntAsync(null, 0, null, null, CancellationToken.None), "amount");
    }

    [Fact]
    public async Task Null_output_into_non_nullable_enum_throws_naming_the_parameter()
    {
        await CreateNullOutputProcAsync();
        var repo = new SqlServerStoredProcedureRepo(_fx.Connection);

        await AssertNullOutputThrowsAsync(
            () => repo.NullIntoEnumAsync(null, null, Status.Pending, null, CancellationToken.None), "state");
    }

    [Fact]
    public async Task Null_output_into_non_nullable_value_object_throws_naming_the_parameter()
    {
        await CreateNullOutputProcAsync();
        var repo = new SqlServerStoredProcedureRepo(_fx.Connection);

        await AssertNullOutputThrowsAsync(
            () => repo.NullIntoValueObjectAsync(null, null, null, new OrderId(0), CancellationToken.None), "orderref");
    }

    [Fact]
    public async Task Null_output_into_nullable_targets_reads_back_as_null()
    {
        await CreateNullOutputProcAsync();
        var repo = new SqlServerStoredProcedureRepo(_fx.Connection);

        var result = await repo.NullIntoNullableAsync("seed", 1, Status.Cancelled, new OrderId(1), CancellationToken.None);

        Assert.Null(result.Note);
        Assert.Null(result.Amount);
        Assert.Null(result.State);
        Assert.Null(result.Orderref);
    }

    private static async Task AssertNullOutputThrowsAsync(Func<Task> call, string parameterName)
    {
        var ex = await Assert.ThrowsAsync<ZeroAllocOrmMaterializationException>(call).ConfigureAwait(false);
        Assert.Contains("'dbo.null_output_proc'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"'{parameterName}'", ex.Message, StringComparison.Ordinal);
    }

    private Task CreateNullOutputProcAsync() => ExecuteAsync("""
        CREATE PROCEDURE dbo.null_output_proc
            @note NVARCHAR(100) OUTPUT,
            @amount INT OUTPUT,
            @state INT OUTPUT,
            @orderref INT OUTPUT
        AS
        BEGIN
            SET @note = NULL;
            SET @amount = NULL;
            SET @state = NULL;
            SET @orderref = NULL;
        END
        """);

    [Fact]
    public async Task Fixed_length_outputs_with_a_Size_round_trip()
    {
        await CreateFixedLengthProcAsync();

        var repo = new SqlServerStoredProcedureRepo(_fx.Connection);
        var (code, ansiCode) = await repo.FixedLengthAsync("", "", CancellationToken.None);

        // NCHAR(10) and CHAR(4) pad to their declared length.
        Assert.Equal("AB        ", code);
        Assert.Equal("XY  ", ansiCode);
    }

    // The provider behaviour behind ZAO066's fixed-length rule, pinned with a
    // hand-written command. A size of 0 fails SqlClient's own check before
    // anything is sent, which is why the generator refuses a fixed-length output
    // without a Size rather than emitting one.
    [Fact]
    public async Task SqlClient_rejects_a_fixed_length_output_of_size_zero()
    {
        await CreateFixedLengthProcAsync();

        var cmd = _fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "dbo.fixed_length_proc";
            cmd.CommandType = CommandType.StoredProcedure;
            AddFixedLengthOutputs(cmd, codeSize: 0);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false));

            Assert.Contains("the Size property has an invalid size of 0", ex.Message, StringComparison.Ordinal);
        }
    }

    // -1 on NCHAR is accepted, so ZAO066 lets an explicit [Param(Size = -1)]
    // through. The generator still does not default a fixed-length output to -1,
    // because that is a MAX declaration, not the fixed length the author wrote.
    [Fact]
    public async Task SqlClient_accepts_a_fixed_length_output_of_size_minus_one()
    {
        await CreateFixedLengthProcAsync();

        var cmd = _fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "dbo.fixed_length_proc";
            cmd.CommandType = CommandType.StoredProcedure;
            var code = AddFixedLengthOutputs(cmd, codeSize: -1);

            await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.Equal("AB        ", code.Value);
        }
    }

    private static IDbDataParameter AddFixedLengthOutputs(System.Data.Async.IAsyncDbCommand cmd, int codeSize)
    {
        var code = cmd.CreateParameter();
        code.ParameterName = "code";
        code.DbType = DbType.StringFixedLength;
        code.Size = codeSize;
        code.Direction = ParameterDirection.Output;
        cmd.Parameters.Add(code);

        var ansiCode = cmd.CreateParameter();
        ansiCode.ParameterName = "ansiCode";
        ansiCode.DbType = DbType.AnsiStringFixedLength;
        ansiCode.Size = 4;
        ansiCode.Direction = ParameterDirection.Output;
        cmd.Parameters.Add(ansiCode);
        return code;
    }

    private Task CreateFixedLengthProcAsync() => ExecuteAsync("""
        CREATE PROCEDURE dbo.fixed_length_proc @code NCHAR(10) OUTPUT, @ansiCode CHAR(4) OUTPUT
        AS
        BEGIN
            SET @code = N'AB';
            SET @ansiCode = 'XY';
        END
        """);

    // #234 could only pin SqlClient's contract with a hand-written command,
    // because the generated code failed. These two run the generated code.
    [Fact]
    public async Task Generated_output_parameters_bind_by_unprefixed_name()
    {
        await CreateScaleProcAsync();

        var repo = new SqlServerStoredProcedureRepo(_fx.Connection);
        var (doubled, tripled) = await repo.ScaleAsync(21, 0, 0, CancellationToken.None);

        Assert.Equal(42, doubled);
        Assert.Equal(63, tripled);
    }

    [Fact]
    public async Task Generated_output_parameters_bind_by_name_out_of_declaration_order()
    {
        await CreateScaleProcAsync();

        // The C# method declares (tripled, doubled, amount), the reverse of the
        // procedure. Binding by position would put 21 into @tripled.
        var repo = new SqlServerStoredProcedureRepo(_fx.Connection);
        var (tripled, doubled) = await repo.ScaleReversedAsync(0, 0, 21, CancellationToken.None);

        Assert.Equal(63, tripled);
        Assert.Equal(42, doubled);
    }

    private Task CreateScaleProcAsync() => ExecuteAsync("""
        CREATE PROCEDURE dbo.scale_proc @amount INT, @doubled INT OUTPUT, @tripled INT OUTPUT
        AS
        BEGIN
            SET @doubled = @amount * 2;
            SET @tripled = @amount * 3;
        END
        """);

    private async Task ExecuteAsync(string sql)
    {
        var cmd = _fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(default).ConfigureAwait(false);
        }
    }
}
