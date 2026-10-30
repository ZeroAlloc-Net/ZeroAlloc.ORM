using System.Data.Common;

namespace ZeroAlloc.ORM.Integration.Tests.AllocationGates;

// #351 — the gated workloads written by hand against the provider's own DbConnection,
// as the benchmark suite's HandWrittenAdoNet rows are. `names` are spelled the way the
// provider resolves for free; see WorkloadNames. The list is unsized, as the
// generated code's is, because neither knows the row count in advance.
internal static class HandWrittenAdoNet
{
    public static async Task<OrderRow?> GetByIdAsync(DbConnection connection, WorkloadNames names, int id)
    {
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = WorkloadSql.SingleRowRead;
            AddParameter(cmd.CreateParameter(), cmd.Parameters, names.Id, id);
            var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                if (!await reader.ReadAsync().ConfigureAwait(false)) return null;
                return new OrderRow(reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2));
            }
        }
    }

    public static async Task<int> InsertAsync(DbConnection connection, WorkloadNames names, int id, int cust, decimal total)
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

    // One command holding both statements, the shape the generated code runs where the
    // provider has no batch support.
    public static async Task<(OrderRow Head, List<OrderLineRow> Lines)?> GetOrderWithLinesAsync(
        DbConnection connection, WorkloadNames names, int id)
    {
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = WorkloadSql.MultiResult;
            AddParameter(cmd.CreateParameter(), cmd.Parameters, names.Id, id);
            var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
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

    private static void AddParameter(DbParameter parameter, DbParameterCollection parameters, string name, object value)
    {
        parameter.ParameterName = name;
        parameter.Value = value;
        parameters.Add(parameter);
    }
}
