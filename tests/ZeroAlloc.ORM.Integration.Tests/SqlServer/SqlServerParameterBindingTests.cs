using System.Data;
using Xunit;

namespace ZeroAlloc.ORM.Integration.Tests.SqlServer;

// v2.0, #219 — SqlClient prepends `@` to a ParameterName that lacks one, both
// in the sp_executesql parameter declaration a text command sends and in the
// argument names of a stored-procedure RPC. The generator's unprefixed names
// therefore bind exactly as the v1 `@`-prefixed ones did.
public sealed class SqlServerParameterBindingTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fx = new();

    public ValueTask InitializeAsync() => _fx.InitializeAsync();
    public ValueTask DisposeAsync() => _fx.DisposeAsync();

    [Fact]
    public async Task Scalar_composite_and_override_parameters_bind_to_at_placeholders()
    {
        await ExecuteAsync(ParameterBindingRepo.OrdersDdl);

        var repo = new ParameterBindingRepo(_fx.Connection);
        var id = await repo.FirstMatchAsync(7, new Money(50m, "EUR"), excludedId: 1, CancellationToken.None);

        Assert.Equal(ParameterBindingRepo.ExpectedMatch, id);
    }

    [Fact]
    public async Task Stored_procedure_binds_input_parameters_by_unprefixed_name()
    {
        await ExecuteAsync("""
            CREATE PROCEDURE dbo.subtract_proc @amount INT, @subtrahend INT
            AS
                SELECT @amount - @subtrahend;
            """);

        // The C# method declares (subtrahend, amount), the reverse of the
        // procedure. 100 - 1 = 99 proves the RPC bound by name; binding by
        // position would compute 1 - 100.
        var repo = new SqlServerParameterBindingProcedureRepo(_fx.Connection);
        var result = await repo.SubtractAsync(subtrahend: 1, amount: 100, CancellationToken.None);

        Assert.Equal(99, result);
    }

    // Provider contract, not generator output. The generated code for a
    // stored-procedure output parameter sets no DbType, and SqlClient rejects
    // that before any name is sent; see the #219 report. This test pins what
    // the generator relies on once that is fixed: OUTPUT parameters named
    // without `@` and added out of declaration order bind by name and have
    // their values copied back.
    [Fact]
    public async Task SqlClient_binds_unprefixed_output_parameter_names()
    {
        await ExecuteAsync("""
            CREATE PROCEDURE dbo.scale_proc @amount INT, @doubled INT OUTPUT, @tripled INT OUTPUT
            AS
            BEGIN
                SET @doubled = @amount * 2;
                SET @tripled = @amount * 3;
            END
            """);

        var cmd = _fx.Connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = "dbo.scale_proc";
            cmd.CommandType = CommandType.StoredProcedure;

            var tripled = cmd.CreateParameter();
            tripled.ParameterName = "tripled";
            tripled.DbType = DbType.Int32;
            tripled.Direction = ParameterDirection.Output;
            cmd.Parameters.Add(tripled);

            var doubled = cmd.CreateParameter();
            doubled.ParameterName = "doubled";
            doubled.DbType = DbType.Int32;
            doubled.Direction = ParameterDirection.Output;
            cmd.Parameters.Add(doubled);

            var amount = cmd.CreateParameter();
            amount.ParameterName = "amount";
            amount.DbType = DbType.Int32;
            amount.Value = 21;
            cmd.Parameters.Add(amount);

            await cmd.ExecuteNonQueryAsync(CancellationToken.None);

            Assert.Equal(42, doubled.Value);
            Assert.Equal(63, tripled.Value);
        }
    }

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
