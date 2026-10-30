using System.Data;
using System.Data.Async;

namespace ZeroAlloc.ORM.Integration.Tests.AllocationGates;

// #351 — the gated workloads written by hand against IAsyncDbConnection, the substrate the
// generated code runs on. A gap between these and the generated methods is the generator's;
// a gap between these and HandWrittenAdoNet is AdoNet.Async's. Each mirrors the generated
// method's calls: the same names, an unsized list, and a batch where the provider has one.
internal static class HandWrittenAsyncDb
{
    public static async Task<OrderRow?> GetByIdAsync(IAsyncDbConnection connection, WorkloadNames names, int id)
    {
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = WorkloadSql.SingleRowRead;
            AddParameter(cmd.CreateParameter(), cmd.Parameters, names.Id, id);
            var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            await using (((IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
            {
                if (!await reader.ReadAsync().ConfigureAwait(false)) return null;
                return new OrderRow(reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2));
            }
        }
    }

    public static async Task<int> InsertAsync(
        IAsyncDbConnection connection, WorkloadNames names, int id, int cust, decimal total)
    {
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = WorkloadSql.Insert;
            AddParameter(cmd.CreateParameter(), cmd.Parameters, names.Id, id);
            AddParameter(cmd.CreateParameter(), cmd.Parameters, names.Cust, cust);
            AddParameter(cmd.CreateParameter(), cmd.Parameters, names.Total, total);
            return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    public static async Task<(OrderRow Head, List<OrderLineRow> Lines)?> GetOrderWithLinesAsync(
        IAsyncDbConnection connection, WorkloadNames names, int id)
    {
        if (connection.CanCreateBatch)
        {
            var batch = connection.CreateBatch();
            await using (batch.ConfigureAwait(false))
            {
                var headCmd = batch.CreateBatchCommand();
                headCmd.CommandText = WorkloadSql.HeadStatement;
                AddParameter(headCmd.CreateParameter(), headCmd.Parameters, names.Id, id);
                batch.BatchCommands.Add(headCmd);
                var linesCmd = batch.CreateBatchCommand();
                linesCmd.CommandText = WorkloadSql.LinesStatement;
                AddParameter(linesCmd.CreateParameter(), linesCmd.Parameters, names.Id, id);
                batch.BatchCommands.Add(linesCmd);
                var reader = await batch.ExecuteReaderAsync().ConfigureAwait(false);
                await using (((IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
                {
                    if (!await reader.ReadAsync().ConfigureAwait(false)) return null;
                    var head = new OrderRow(reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2));
                    await reader.NextResultAsync().ConfigureAwait(false);
                    var lines = new List<OrderLineRow>();
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        lines.Add(new OrderLineRow(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3)));
                    }
                    return (head, lines);
                }
            }
        }

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = WorkloadSql.MultiResult;
            AddParameter(cmd.CreateParameter(), cmd.Parameters, names.Id, id);
            var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            await using (((IAsyncDisposable)reader).ConfigureAwaitAsDisposable())
            {
                if (!await reader.ReadAsync().ConfigureAwait(false)) return null;
                var head = new OrderRow(reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2));
                await reader.NextResultAsync().ConfigureAwait(false);
                var lines = new List<OrderLineRow>();
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    lines.Add(new OrderLineRow(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3)));
                }
                return (head, lines);
            }
        }
    }

    private static void AddParameter(IDbDataParameter parameter, IDataParameterCollection parameters, string name, object value)
    {
        parameter.ParameterName = name;
        parameter.Value = value;
        parameters.Add(parameter);
    }
}
